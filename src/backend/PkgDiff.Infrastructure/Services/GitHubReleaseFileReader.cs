using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

// Release notes kept as one file per version in a repository folder (listed once per repo)
public class GitHubReleaseFileReader
{
    private static readonly string[] Folders =
    {
        "docs/releases", "docs/release-notes", "docs/release_notes", "docs/releasenotes",
        "doc/releases", "doc/release-notes", "release-notes", "releasenotes", "changelogs",
        "docs/changelogs",
    };

    private static readonly string[] Extensions = { ".md", ".markdown", ".rst", ".txt" };

    private readonly HttpClient _httpClient;
    private readonly IRateLimiterService _rateLimiter;
    private readonly IGitHubRateLimitHandler _rateLimitHandler;
    private readonly GitHubScrapeCache _cache;
    private readonly bool _useGitHubApi;
    private readonly ILogger _logger;

    public GitHubReleaseFileReader(
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

    public async Task<string?> FetchAsync(string githubRepoUrl, string version)
    {
        if (!_useGitHubApi)
            return null;

        var (owner, repo) = GitHubUrls.Parse(githubRepoUrl, _logger);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            return null;

        var files = await ReleaseFilesAsync(owner, repo);
        var path = FileFor(files, version.TrimStart('v', 'V'));
        if (path is null)
            return null;

        var rawUrl = $"https://raw.githubusercontent.com/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/HEAD/{GitHubUrls.EscapeFilePath(path)}";
        try
        {
            using var response = await _rateLimiter.ExecuteAsync("github-scraping", async () => await _httpClient.GetAsync(rawUrl));
            if (!response.IsSuccessStatusCode)
                return null;

            var notes = WithoutTitle(await response.Content.ReadAsStringAsync());
            if (!string.IsNullOrWhiteSpace(notes))
                _logger.LogDebug("[GitHubScraper] Release notes for {Version} found in {Path} of {Owner}/{Repo}", version, path, owner, repo);
            return string.IsNullOrWhiteSpace(notes) ? null : notes;
        }
        catch (OperationCanceledException) when (WorkCancellation.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[GitHubScraper] Failed to fetch the release file {Url}", rawUrl);
            return null;
        }
    }

    // A trailing ".0" is optional: x.y and x.y.0 name the same file
    private static string? FileFor(Dictionary<string, string> files, string version)
    {
        if (files.TryGetValue(version, out var path))
            return path;
        if (version.EndsWith(".0", StringComparison.Ordinal) && version.Count(c => c == '.') == 2
            && files.TryGetValue(version[..^2], out path))
            return path;
        if (version.Count(c => c == '.') == 1 && files.TryGetValue(version + ".0", out path))
            return path;
        return null;
    }

    // Version -> file path, from the first folder that holds files named after versions; empty when none does
    private async Task<Dictionary<string, string>> ReleaseFilesAsync(string owner, string repo)
    {
        var repoKey = $"{owner}/{repo}".ToLowerInvariant();
        if (_cache.TryGetReleaseFiles(repoKey, out var cached))
            return cached;

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders)
        {
            var listing = await ListAsync(owner, repo, folder);
            if (listing is null)
                return files; // the API failed: asked again next time rather than remembered as empty

            foreach (var (name, path) in listing)
            {
                var extension = Extensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
                if (extension is null)
                    continue;

                var stem = name[..^extension.Length].TrimStart('v', 'V');
                if (stem.Length > 0 && char.IsDigit(stem[0]) && stem.Contains('.'))
                    files.TryAdd(stem, path);
            }

            // A folder with a handful of version files is the one; a few stray names are not
            if (files.Count >= 3)
                break;
            files.Clear();
        }

        _cache.SetReleaseFiles(repoKey, files);
        _logger.LogDebug("[GitHubScraper] {Count} per-version release files in {RepoKey}", files.Count, repoKey);
        return files;
    }

    // The files of a folder; empty when it does not exist, null when the API could not be asked
    private async Task<List<(string Name, string Path)>?> ListAsync(string owner, string repo, string folder)
    {
        var apiUrl = $"https://api.github.com/repos/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/contents/{GitHubUrls.EscapeFilePath(folder)}";
        try
        {
            await _rateLimitHandler.CanMakeRequestAsync();
            using var response = await _rateLimiter.ExecuteAsync("github-api", async () => await _httpClient.GetAsync(apiUrl));
            await _rateLimitHandler.UpdateFromHeadersAsync(response);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new List<(string, string)>();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[GitHubScraper] API returned {StatusCode} for {Url}", response.StatusCode, apiUrl);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new List<(string, string)>();

            var files = new List<(string, string)>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.GetString() == "file"
                    && item.TryGetProperty("name", out var name) && item.TryGetProperty("path", out var path)
                    && name.GetString() is { } fileName && path.GetString() is { } filePath)
                    files.Add((fileName, filePath));
            }
            return files;
        }
        catch (OperationCanceledException) when (WorkCancellation.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GitHubScraper] Failed to list {Url}", apiUrl);
            return null;
        }
    }

    // The file's title repeats the version: an RST title between rules or a first Markdown heading
    private static string WithoutTitle(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);

        static bool IsRule(string line) => line.Trim().Length >= 3 && line.Trim().All(c => c is '=' or '-' or '~' or '*' or '#');

        if (lines.Count >= 3 && IsRule(lines[0]) && IsRule(lines[2]))
            lines.RemoveRange(0, 3);
        else if (lines.Count >= 2 && IsRule(lines[1]))
            lines.RemoveRange(0, 2);
        else if (lines.Count >= 1 && lines[0].StartsWith("# ", StringComparison.Ordinal))
            lines.RemoveAt(0);

        return string.Join('\n', lines).Trim();
    }
}
