using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Security;
using Polly;
using Polly.Retry;

namespace PkgDiff.Infrastructure.Services;

public class ReleaseNotesUrlResolver : IReleaseNotesUrlResolver
{
    private readonly IGitHubScraperService _githubScraperService;
    private readonly IHtmlDocumentLoader _htmlDocumentLoader;
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger<ReleaseNotesUrlResolver> _logger;

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s""'<>\)]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex GitHubReleasesRegex = new(
        @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex GitHubBlobRegex = new(
        @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/blob/(?<branch>[^/]+)/(?<path>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex GitHubRepoRegex = new(
        @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly string[] ChangelogFilenames =
        ["changelog", "changes", "release-notes", "releasenotes", "history"];

    private static readonly string[] FillerPhrases =
    [
        "see", "please see", "please read", "read the",
        "full release notes", "release notes", "upgrade guide",
        "for details", "for more details", "for more information",
        "can be found at", "available at", "found at",
        "changelog", "change log", "changes",
        "is a major release", "is a minor release"
    ];

    public ReleaseNotesUrlResolver(
        IGitHubScraperService githubScraperService,
        IHtmlDocumentLoader htmlDocumentLoader,
        HttpClient httpClient,
        ILogger<ReleaseNotesUrlResolver> logger)
    {
        _githubScraperService = githubScraperService;
        _htmlDocumentLoader = htmlDocumentLoader;
        _httpClient = httpClient;
        _logger = logger;

        // Only transient failures are retried; a 404 or an SSRF-blocked target is final
        _retryPolicy = Policy
            .Handle<Exception>(TransientHttp.IsTransient)
            .WaitAndRetryAsync(2, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<UrlResolverResult> ResolveAsync(
        string rawReleaseNotes,
        string packageId,
        string version,
        string? githubRepoUrl)
    {
        if (string.IsNullOrWhiteSpace(rawReleaseNotes))
            return new UrlResolverResult(rawReleaseNotes, false);

        var urls = ExtractUrls(rawReleaseNotes);
        if (urls.Count == 0)
            return new UrlResolverResult(rawReleaseNotes, false);

        if (!IsUrlDominated(rawReleaseNotes, urls))
            return new UrlResolverResult(rawReleaseNotes, false);

        _logger.LogDebug(
            "[UrlResolver] Detected URL-dominated release notes for {PackageId} v{Version}, attempting resolution...",
            packageId, version);

        // Capped: each link may wait on a slow host and a note can list hundreds
        var classifiedUrls = ClassifyUrls(urls).Take(MaxUrlsFollowed);

        foreach (var (url, urlType) in classifiedUrls)
        {
            try
            {
                var result = await ResolveUrlAsync(url, urlType, version);
                if (result != null && !string.IsNullOrWhiteSpace(result.ReleaseNotes))
                {
                    _logger.LogDebug(
                        "[UrlResolver] Successfully resolved release notes for {PackageId} v{Version} from {Source}",
                        packageId, version, result.ResolvedSource);
                    return result;
                }
            }
            catch (GitHubRateLimitedException)
            {
                // Rethrown so the version is scraped again once the rate limit resets
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[UrlResolver] Failed to resolve URL {Url} for {PackageId} v{Version}",
                    url, packageId, version);
            }
        }

        return new UrlResolverResult(rawReleaseNotes, false);
    }

    private const int MaxUrlsFollowed = 3;

    private static List<string> ExtractUrls(string text)
    {
        return UrlRegex.Matches(text)
            .Select(m => m.Value.TrimEnd('.', ',', ';', ')'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsUrlDominated(string text, List<string> urls)
    {
        var strippedText = text;
        foreach (var url in urls)
            strippedText = strippedText.Replace(url, "");

        var remaining = strippedText;
        foreach (var filler in FillerPhrases)
            remaining = Regex.Replace(remaining, Regex.Escape(filler), "", RegexOptions.IgnoreCase);

        remaining = Regex.Replace(remaining, @"[^\w]", "");

        return remaining.Length < 80;
    }

    private static List<(string Url, UrlType Type)> ClassifyUrls(List<string> urls)
    {
        var classified = new List<(string Url, UrlType Type)>();

        foreach (var url in urls)
        {
            if (GitHubReleasesRegex.IsMatch(url))
                classified.Add((url, UrlType.GitHubReleases));
            else if (GitHubBlobRegex.IsMatch(url) && IsChangelogPath(url))
                classified.Add((url, UrlType.GitHubChangelog));
            else if (GitHubBlobRegex.IsMatch(url))
                classified.Add((url, UrlType.GitHubBlob));
            else if (GitHubRepoRegex.IsMatch(url))
                classified.Add((url, UrlType.GitHubRepo));
            else
                classified.Add((url, UrlType.ExternalPage));
        }

        return classified.OrderBy(c => c.Type).ToList();
    }

    private static bool IsChangelogPath(string url)
    {
        var match = GitHubBlobRegex.Match(url);
        if (!match.Success) return false;

        var path = match.Groups["path"].Value.ToLowerInvariant();
        return ChangelogFilenames.Any(name => path.Contains(name));
    }

    private async Task<UrlResolverResult?> ResolveUrlAsync(
        string url, UrlType urlType, string version)
    {
        return urlType switch
        {
            UrlType.GitHubReleases => await ResolveGitHubReleasesAsync(url, version),
            UrlType.GitHubChangelog => await ResolveGitHubChangelogAsync(url, version),
            UrlType.GitHubRepo => await ResolveGitHubRepoAsync(url, version),
            UrlType.GitHubBlob => await ResolveGitHubBlobAsync(url, version),
            UrlType.ExternalPage => await ResolveExternalPageAsync(url),
            _ => null
        };
    }

    private async Task<UrlResolverResult?> ResolveGitHubReleasesAsync(string url, string version)
    {
        var repoUrl = url.Replace("/releases", "").TrimEnd('/');

        var notes = await _githubScraperService.GetReleaseNotesForVersionAsync(repoUrl, version);
        if (!string.IsNullOrWhiteSpace(notes))
            return new UrlResolverResult(notes, true, "GitHub Release", repoUrl);

        return null;
    }

    private async Task<UrlResolverResult?> ResolveGitHubRepoAsync(string url, string version)
    {
        var repoUrl = url.TrimEnd('/');

        var notes = await _githubScraperService.GetReleaseNotesForVersionAsync(repoUrl, version);
        if (!string.IsNullOrWhiteSpace(notes))
            return new UrlResolverResult(notes, true, "GitHub Release", repoUrl);

        return null;
    }

    private async Task<UrlResolverResult?> ResolveGitHubChangelogAsync(string url, string version)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var rawUrl = ConvertToRawGitHubUrl(url);
            if (rawUrl == null) return null;

            var content = await _httpClient.GetStringAsync(rawUrl);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var versionSection = ExtractVersionSection(content, version);
            if (!string.IsNullOrWhiteSpace(versionSection))
            {
                var match = GitHubBlobRegex.Match(url);
                var repoUrl = match.Success
                    ? $"https://github.com/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}"
                    : null;

                return new UrlResolverResult(versionSection, true, "GitHub CHANGELOG", repoUrl);
            }

            return null;
        });
    }

    private async Task<UrlResolverResult?> ResolveGitHubBlobAsync(string url, string version)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var rawUrl = ConvertToRawGitHubUrl(url);
            if (rawUrl == null) return null;

            var content = await _httpClient.GetStringAsync(rawUrl);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var versionSection = ExtractVersionSection(content, version);
            var result = !string.IsNullOrWhiteSpace(versionSection) ? versionSection : content;

            if (result.Length > 5000)
                result = result[..5000] + "\n... (truncated)";

            return new UrlResolverResult(result, true, "GitHub File");
        });
    }

    private async Task<UrlResolverResult?> ResolveExternalPageAsync(string url)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !DocumentationHostAllowList.IsAllowed(uri.Host))
                return null;

            using var document = await _htmlDocumentLoader.LoadAsync(url);
            if (document == null) return null;

            var contentSelectors = new[]
            {
                "article",
                "main",
                "[role='main']",
                ".content",
                ".markdown-body",
                "#content",
                ".post-content",
                ".entry-content",
                ".document-content",
                ".rst-content"
            };

            foreach (var selector in contentSelectors)
            {
                var element = document.QuerySelector(selector);
                if (element == null || string.IsNullOrWhiteSpace(element.TextContent))
                    continue;

                var text = CleanExtractedText(element.TextContent);
                if (text.Length <= 100) continue;

                if (text.Length > 5000)
                    text = text[..5000] + "\n... (truncated)";

                var host = new Uri(url).Host;
                return new UrlResolverResult(text, true, $"External ({host})");
            }

            return null;
        });
    }

    private static string? ConvertToRawGitHubUrl(string blobUrl)
    {
        var match = GitHubBlobRegex.Match(blobUrl);
        if (!match.Success) return null;

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var branch = match.Groups["branch"].Value;
        var path = match.Groups["path"].Value;

        return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
    }

    internal static string? ExtractVersionSection(string changelogContent, string version)
    {
        var cleanVersion = version.TrimStart('v', 'V');
        var lines = changelogContent.Split('\n');

        var escapedVersion = Regex.Escape(cleanVersion);
        var versionHeadingPattern = new Regex(
            @"^#{1,4}\s+\[?" + "v?" + escapedVersion + @"\]?",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        int sectionStart = -1;
        int sectionHeadingLevel = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();

            if (sectionStart == -1)
            {
                if (versionHeadingPattern.IsMatch(line))
                {
                    sectionStart = i + 1;
                    sectionHeadingLevel = CountHeadingLevel(line);
                }
            }
            else
            {
                if (line.StartsWith('#'))
                {
                    var currentLevel = CountHeadingLevel(line);
                    if (currentLevel <= sectionHeadingLevel)
                    {
                        return string.Join('\n', lines[sectionStart..i]).Trim();
                    }
                }
            }
        }

        if (sectionStart != -1)
            return string.Join('\n', lines[sectionStart..]).Trim();

        return null;
    }

    private static int CountHeadingLevel(string line)
    {
        int count = 0;
        foreach (var ch in line)
        {
            if (ch == '#') count++;
            else break;
        }
        return count;
    }

    private static string CleanExtractedText(string text)
    {
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        return text.Trim();
    }

    private enum UrlType
    {
        GitHubReleases = 0,
        GitHubChangelog = 1,
        GitHubRepo = 2,
        GitHubBlob = 3,
        ExternalPage = 4
    }
}
