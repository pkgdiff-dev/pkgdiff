using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

// Finds the notes of a version in the changelog file of a repository, for releases whose own notes
// are empty or only point to the changelog
public partial class GitHubChangelogReader
{
    private static readonly string[] CandidateFiles =
    {
            "CHANGELOG.md", "changelog.md", 
            "HISTORY.md", "History.md", 
            "CHANGES.md", "changes.md", 
            "CHANGELOG.rst", "changelog.rst", 
            "HISTORY.rst", "History.rst", 
            "CHANGES.rst", "changes.rst", 
            "CHANGELOG.txt", "changelog.txt",
            "HISTORY.txt", "History.txt",
            "RELEASES.md", "releases.md",
            "RELEASES.rst", "releases.rst",
            "RELEASES.txt", "releases.txt",
            "RELEASE.md", "release.md",
            "RELEASE.txt", "release.txt",
            "RELEASE.rst", "release.rst"
    };

    private readonly HttpClient _httpClient;
    private readonly IRateLimiterService _rateLimiter;
    private readonly IGitHubRateLimitHandler _rateLimitHandler;
    private readonly GitHubScrapeCache _cache;
    private readonly bool _useGitHubApi;
    private readonly ILogger _logger;

    public GitHubChangelogReader(
        HttpClient httpClient,
        IRateLimiterService rateLimiter,
        IGitHubRateLimitHandler rateLimitHandler,
        GitHubScrapeCache cache,
        bool useGitHubApi,
        ILogger logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _rateLimitHandler = rateLimitHandler;
        _cache = cache;
        _useGitHubApi = useGitHubApi;
        _logger = logger;
    }

    public static bool IsRedirection(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return false;
        
        // If notes are very long, they probably contain actual notes alongside a link
        if (notes.Length > 250) return false;
        
        var lower = notes.ToLowerInvariant();
        
        // Check if it contains changelog/history/changes reference
        if (lower.Contains("changelog") || lower.Contains("history") || lower.Contains("changes"))
        {
            // Check for redirection indicators
            if (lower.Contains("see") || 
                lower.Contains("refer") || 
                lower.Contains("view") || 
                lower.Contains("full") || 
                lower.Contains("details") ||
                lower.Contains("release notes in"))
            {
                return true;
            }
        }
        
        return false;
    }

    // Where a changelog can be: the folder of the package in a monorepo (from the repository URL,
    // e.g. .../tree/main/packages/core), the repository root, and the repository wiki
    public async Task<string?> FetchAsync(string githubRepoUrl, string version, string? packageId = null)
    {
        var (owner, repo) = GitHubUrls.Parse(githubRepoUrl, _logger);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo)) return null;

        var folder = GitHubUrls.SubPath(githubRepoUrl);
        if (!string.IsNullOrEmpty(folder))
        {
            var inFolder = await FetchFromFolderAsync(owner, repo, folder, version, packageId);
            if (!string.IsNullOrWhiteSpace(inFolder))
                return inFolder;
        }

        return await FetchFromFolderAsync(owner, repo, string.Empty, version, packageId)
               ?? await FetchFromWikiAsync(owner, repo, version, packageId);
    }

    private async Task<string?> FetchFromFolderAsync(string owner, string repo, string folder, string version, string? packageId)
    {
        var repoKey = (folder.Length == 0 ? $"{owner}/{repo}" : $"{owner}/{repo}/{folder}").ToLowerInvariant();
        var knowledge = _cache.Changelog(repoKey);
        IReadOnlyList<string> changelogFiles = CandidateFiles;

        // Try API first to get exact list of existing changelog files
        await ResolveExistingFilesViaApiAsync(owner, repo, folder, repoKey, knowledge);

        var existingFiles = knowledge.ExistingFiles();
        if (knowledge.IsFullyResolved && existingFiles != null)
        {
            // If the cache says there are no changelog files, we can skip immediately!
            if (existingFiles.Count == 0)
            {
                _logger.LogDebug("[GitHubScraper] Skipping changelog search for {RepoKey} because cache indicates no changelog files exist.", repoKey);
                return null;
            }

            // Otherwise, only try files that are known to exist
            changelogFiles = existingFiles;
        }
        else if (existingFiles is { Count: > 0 })
        {
            // Prioritize already discovered existing files even if repository is not fully resolved
            var prioritizedList = new List<string>(existingFiles);
            foreach (var file in CandidateFiles)
            {
                if (!prioritizedList.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    prioritizedList.Add(file);
                }
            }
            changelogFiles = prioritizedList;
        }

        var cleanVersion = version.TrimStart('v', 'V');
        // The version tag, then the default branch: HEAD is whichever branch the repository uses (master, main, develop)
        var refNames = new[] { $"v{cleanVersion}", cleanVersion, "HEAD" };

        var attemptedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var succeededFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in changelogFiles)
        {
            // Skip if we already know this file does not exist in this repo
            if (knowledge.IsMissing(fileName))
            {
                continue;
            }

            attemptedFiles.Add(fileName);

            foreach (var refName in refNames)
            {
                var filePath = folder.Length == 0 ? fileName : $"{folder}/{fileName}";
                var rawUrl = $"https://raw.githubusercontent.com/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/{PackageUrlEncoder.EscapeSegment(refName)}/{GitHubUrls.EscapeFilePath(filePath)}";
                try
                {
                    using var response = await _rateLimiter.ExecuteAsync("github-scraping", async () =>
                        await _httpClient.GetAsync(rawUrl));

                    if (response.IsSuccessStatusCode)
                    {
                        succeededFiles.Add(fileName);

                        // Since it succeeded, we know this file exists
                        knowledge.AddExisting(fileName);

                        var content = await response.Content.ReadAsStringAsync();
                        var extractedNotes = ExtractVersionFromChangelog(content, version, packageId);
                        if (!string.IsNullOrWhiteSpace(extractedNotes))
                        {
                            _logger.LogDebug("[GitHubScraper] Successfully extracted release notes for {Version} from {FileName} at ref {Ref}",
                                version, fileName, refName);

                            RecordFailures(repoKey, knowledge, attemptedFiles, succeededFiles);
                            return extractedNotes;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Missing on the default branch: the file is not in the repository
                        if (refName == "HEAD")
                        {
                            knowledge.MarkMissing(fileName);
                            _logger.LogDebug("[GitHubScraper] Marked {FileName} as non-existent for {RepoKey} (404 on default branch {Ref})",
                                fileName, repoKey, refName);
                        }
                    }
                }
                catch (OperationCanceledException) when (WorkCancellation.Token.IsCancellationRequested)
                {
                    // The work this lookup belongs to was cancelled: no point in trying the other files
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[GitHubScraper] Failed to fetch {FileName} from {Url}", fileName, rawUrl);
                }
            }
        }

        RecordFailures(repoKey, knowledge, attemptedFiles, succeededFiles);
        return null;
    }

    // GitHub wiki pages are plain Markdown files of a separate repository (lodash keeps its changelog there)
    private static readonly string[] WikiPages = { "Changelog", "CHANGELOG", "Change-Log", "Release-Notes", "Release-notes", "Changes", "History" };

    private async Task<string?> FetchFromWikiAsync(string owner, string repo, string version, string? packageId)
    {
        var knowledge = _cache.Changelog($"{owner}/{repo}/wiki".ToLowerInvariant());
        foreach (var page in WikiPages)
        {
            if (knowledge.IsMissing(page))
                continue;

            var rawUrl = $"https://raw.githubusercontent.com/wiki/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/{PackageUrlEncoder.EscapeSegment(page)}.md";
            try
            {
                using var response = await _rateLimiter.ExecuteAsync("github-scraping", async () => await _httpClient.GetAsync(rawUrl));
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        knowledge.MarkMissing(page);
                    continue;
                }

                knowledge.AddExisting(page);
                var notes = ExtractVersionFromChangelog(await response.Content.ReadAsStringAsync(), version, packageId);
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    _logger.LogDebug("[GitHubScraper] Release notes for {Version} found in the wiki page {Page} of {Owner}/{Repo}", version, page, owner, repo);
                    return notes;
                }
            }
            catch (OperationCanceledException) when (WorkCancellation.Token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[GitHubScraper] Failed to fetch the wiki page {Url}", rawUrl);
            }
        }
        return null;
    }

    private async Task ResolveExistingFilesViaApiAsync(string owner, string repo, string folder, string repoKey, RepoChangelogKnowledge knowledge)
    {
        if (knowledge.ExistingFiles() != null || !_useGitHubApi)
        {
            return;
        }

        var apiUrl = $"https://api.github.com/repos/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/contents"
                     + (folder.Length == 0 ? string.Empty : "/" + GitHubUrls.EscapeFilePath(folder));
        
        try
        {
            await _rateLimitHandler.CanMakeRequestAsync();
            
            using var response = await _rateLimiter.ExecuteAsync("github-api", async () =>
            {
                _logger.LogDebug("[GitHubScraper] API Request for repo contents: GET {ApiUrl}", apiUrl);
                return await _httpClient.GetAsync(apiUrl);
            });

            await _rateLimitHandler.UpdateFromHeadersAsync(response);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var existingList = new List<string>();
                    var candidates = new HashSet<string>(CandidateFiles, StringComparer.OrdinalIgnoreCase);

                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var nameProp) && 
                            item.TryGetProperty("type", out var typeProp) && 
                            typeProp.GetString() == "file")
                        {
                            var name = nameProp.GetString();
                            if (!string.IsNullOrEmpty(name) && candidates.Contains(name))
                            {
                                existingList.Add(name);
                            }
                        }
                    }

                    knowledge.SetResolved(existingList);
                    _logger.LogDebug("[GitHubScraper] Discovered existing changelog files for {RepoKey} via API: [{Files}]", 
                        repoKey, string.Join(", ", existingList));
                }
            }
            else
            {
                _logger.LogWarning("[GitHubScraper] API returned {StatusCode} for repo contents: {Url}", response.StatusCode, apiUrl);
            }
        }
        catch (GitHubRateLimitedException)
        {
            // The candidate files are tried directly instead; raw file downloads do not count against the API limit
            _logger.LogDebug("[GitHubScraper] Rate limited, not listing repo contents of {RepoKey}", repoKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GitHubScraper] Failed to fetch repo contents via API for {RepoKey}", repoKey);
        }

    }


    private void RecordFailures(string repoKey, RepoChangelogKnowledge knowledge, HashSet<string> attemptedFiles, HashSet<string> succeededFiles)
    {
        foreach (var fileName in attemptedFiles)
        {
            if (succeededFiles.Contains(fileName))
            {
                continue;
            }

            var failures = knowledge.RecordFailure(fileName);
            if (failures >= RepoChangelogKnowledge.FailuresBeforeMissing)
            {
                _logger.LogInformation("[GitHubScraper] Marked {FileName} as non-existent for {RepoKey} after {Failures} failed version-scraping attempts",
                    fileName, repoKey, failures);
            }
        }
    }

    private string ExtractVersionFromChangelog(string changelogText, string version, string? packageId)
    {
        if (string.IsNullOrWhiteSpace(changelogText)) return string.Empty;

        var lines = changelogText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var cleanVer = version.TrimStart('v', 'V');
        
        // A heading names the version, optionally after a word ("Version", "Release", the package name),
        // and ends the version with a space, a bracket, a link or the end of the line:
        //   ## v2.34.1      ## [2.34.1] - 2026-05-13      ## [10.0.0](https://...compare/v9.0.0...v10.0.0) (2024-06-07)
        //   ## Version 1.1.4      ## socket.io@4.8.3      ## react-dom 18.3.1      1.1.4 (2021-05-13) + underline
        var title = $@"(?:(?:version|release|v)\s*|[\w@/.-]+(?:\s+v?|@v?))?\[?[vV]?{Regex.Escape(cleanVer)}\]?(?=$|[\s(\[\]:,;])(?:.*)$";
        var atxRegex = new Regex($@"^(?<hashes>#+)\s+{title}", RegexOptions.IgnoreCase);

        // A line that could be a Setext heading, e.g. "2.34.1 (2026-05-13)" or "Version 1.1.4"
        var setextTitleRegex = new Regex("^" + title, RegexOptions.IgnoreCase);
        var setextUnderlineRegex = new Regex(@"^[-=]{3,}$");

        int startIndex = -1;
        bool isAtx = false;
        int hashCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = HtmlTag().Replace(lines[i], string.Empty).Trim();

            // Check ATX first
            var match = atxRegex.Match(line);
            if (match.Success)
            {
                startIndex = i;
                hashCount = match.Groups["hashes"].Value.Length;
                isAtx = true;
                break;
            }
            
            // Check Setext
            if (setextTitleRegex.IsMatch(line) && i + 1 < lines.Length && setextUnderlineRegex.IsMatch(lines[i + 1].Trim()))
            {
                startIndex = i;
                isAtx = false;
                break;
            }
        }

        if (startIndex == -1) return string.Empty;

        var sectionLines = new List<string>();
        
        // If ATX heading of level N: terminate on any heading of level <= N (e.g. # or ##)
        // If Setext heading: terminate on any ATX heading of level 1 or 2, or another Setext heading
        Regex terminatorRegex;
        if (isAtx)
        {
            terminatorRegex = new Regex($@"^#{{1,{hashCount}}}\s+\S+");
        }
        else
        {
            // Setext heading is equivalent to h1 or h2. Terminate on any h1 or h2 (e.g. # or ##)
            terminatorRegex = new Regex(@"^#{1,2}\s+\S+");
        }

        int startReadingFrom = isAtx ? startIndex + 1 : startIndex + 2; // Skip the underline for Setext

        for (int i = startReadingFrom; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var trimmedLine = rawLine.Trim();

            // Check if we hit another header
            if (isAtx)
            {
                if (terminatorRegex.IsMatch(trimmedLine))
                    break;
            }
            else
            {
                // For Setext, we also terminate if we find another Setext header
                if (terminatorRegex.IsMatch(trimmedLine))
                    break;
                
                if (i + 1 < lines.Length && setextUnderlineRegex.IsMatch(lines[i + 1].Trim()))
                {
                    // This line and next line form a Setext header, which terminates our section
                    break;
                }
            }

            sectionLines.Add(rawLine);
        }

        return string.Join("\n", sectionLines).Trim();
    }

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTag();
}
