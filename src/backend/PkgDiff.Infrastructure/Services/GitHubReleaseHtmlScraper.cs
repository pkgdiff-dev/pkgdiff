using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

// Reads releases from the HTML pages of a repository, for when the GitHub API is not used
public class GitHubReleaseHtmlScraper
{
    private readonly IRateLimiterService _rateLimiter;
    private readonly IHtmlDocumentLoader _htmlDocumentLoader;
    private readonly int _htmlScrapingTimeoutSeconds;
    private readonly ILogger _logger;

    public GitHubReleaseHtmlScraper(
        IRateLimiterService rateLimiter,
        IHtmlDocumentLoader htmlDocumentLoader,
        int htmlScrapingTimeoutSeconds,
        ILogger logger)
    {
        _rateLimiter = rateLimiter;
        _htmlDocumentLoader = htmlDocumentLoader;
        _htmlScrapingTimeoutSeconds = htmlScrapingTimeoutSeconds;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> GetAllReleasesAsync(string githubRepoUrl)
    {
        var releases = new Dictionary<string, string>();
        var baseReleasesUrl = $"{GitHubUrls.Clean(githubRepoUrl)}/releases";

        _logger.LogDebug("[GitHubScraper] Fetching all releases via HTML: {ReleasesUrl}", baseReleasesUrl);
        
        // Bounded: on timeout the releases read so far are returned
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_htmlScrapingTimeoutSeconds));
        
        try
        {
            using var document = await _rateLimiter.ExecuteAsync("github-scraping", () => _htmlDocumentLoader.LoadAsync(baseReleasesUrl));
            if (document == null)
            {
                _logger.LogWarning("[GitHubScraper] Document returned null for {ReleasesUrl}", baseReleasesUrl);
                return releases;
            }

            _logger.LogDebug("[GitHubScraper] Page title: {Title}", document.Title);

            GitHubReleasePage.ExtractReleases(document, releases, _logger);

            var totalPages = GitHubReleasePage.GetTotalPages(document);
            _logger.LogDebug("[GitHubScraper] Total pages detected: {TotalPages}", totalPages);

            var maxPagesToScrape = totalPages;
            
            for (int page = 2; page <= maxPagesToScrape; page++)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("[GitHubScraper] TIMEOUT after {Timeout}s - Returning partial results ({ReleasesCount} releases from {PagesScraped}/{TotalPages} pages)", 
                        _htmlScrapingTimeoutSeconds, releases.Count, page - 1, totalPages);
                    
                    return releases;
                }
                
                var pageUrl = $"{baseReleasesUrl}?page={page}";
                _logger.LogDebug("[GitHubScraper] Fetching page {Page}/{Total}: {PageUrl}", page, maxPagesToScrape, pageUrl);

                try
                {
                    using var pageDocument = await _rateLimiter.ExecuteAsync("github-scraping", () => _htmlDocumentLoader.LoadAsync(pageUrl));
                    if (pageDocument != null)
                    {
                        GitHubReleasePage.ExtractReleases(pageDocument, releases, _logger);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[GitHubScraper] Failed to fetch page {Page}, continuing...", page);
                }
            }

            _logger.LogDebug("[GitHubScraper] Total releases extracted via HTML: {Count}. Versions: [{Versions}]",
                releases.Count, string.Join(", ", releases.Keys.Take(20)));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[GitHubScraper] TIMEOUT - Returning partial results ({ReleasesCount} releases)", releases.Count);
        }

        return releases;
    }

    // Lazy: pages are fetched one by one until the version is found, which keeps the pressure on the rate limit low
    public async Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version)
    {
        var baseReleasesUrl = $"{GitHubUrls.Clean(githubRepoUrl)}/releases";
        var cleanVersion = version.TrimStart('v', 'V');
        
        _logger.LogDebug("[GitHubScraper] Using LAZY HTML scraping for version {Version}", cleanVersion);

        var timeoutSeconds = _htmlScrapingTimeoutSeconds / 2;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var document = await _rateLimiter.ExecuteAsync("github-scraping", () => _htmlDocumentLoader.LoadAsync(baseReleasesUrl));
            if (document == null)
            {
                _logger.LogWarning("[GitHubScraper] Document returned null for {ReleasesUrl}", baseReleasesUrl);
                return null;
            }

            var releases = new Dictionary<string, string>();
            GitHubReleasePage.ExtractReleases(document, releases, _logger);
            
            var result = TryFindVersion(releases, cleanVersion);
            if (result != null)
            {
                _logger.LogDebug("[GitHubScraper] Found version {Version} on page 1", cleanVersion);
                return result;
            }

            var totalPages = GitHubReleasePage.GetTotalPages(document);
            
            _logger.LogDebug("[GitHubScraper] Version not found on page 1, checking up to {TotalPages} more pages", totalPages - 1);

            for (int page = 2; page <= totalPages; page++)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("[GitHubScraper] TIMEOUT after {Timeout}s while searching for version {Version} - Version not found in first {Pages} pages", 
                        timeoutSeconds, cleanVersion, page - 1);
                    return null;
                }
                
                var pageUrl = $"{baseReleasesUrl}?page={page}";
                
                try
                {
                    releases.Clear();
                    using var pageDocument = await _rateLimiter.ExecuteAsync("github-scraping", () => _htmlDocumentLoader.LoadAsync(pageUrl));
                    
                    if (pageDocument != null)
                    {
                        GitHubReleasePage.ExtractReleases(pageDocument, releases, _logger);
                        
                        result = TryFindVersion(releases, cleanVersion);
                        if (result != null)
                        {
                            _logger.LogDebug("[GitHubScraper] Found version {Version} on page {Page}", cleanVersion, page);
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[GitHubScraper] Failed to fetch page {Page}, stopping search", page);
                    break;
                }
            }

            _logger.LogWarning("[GitHubScraper] Version {Version} not found after checking {Pages} pages", cleanVersion, totalPages);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[GitHubScraper] TIMEOUT while searching for version {Version}", cleanVersion);
        }
        
        return null;
    }
    
    private static string? TryFindVersion(Dictionary<string, string> releases, string targetVersion)
    {
        if (releases.TryGetValue(targetVersion, out var notes))
        {
            return notes;
        }

        if (releases.TryGetValue($"v{targetVersion}", out notes))
        {
            return notes;
        }

        var fuzzyMatch = releases.FirstOrDefault(kvp =>
            kvp.Key.Replace(".", "").Replace("-", "").Replace("_", "")
                .Equals(targetVersion.Replace(".", "").Replace("-", "").Replace("_", ""),
                    StringComparison.OrdinalIgnoreCase));

        return fuzzyMatch.Value;
    }
}
