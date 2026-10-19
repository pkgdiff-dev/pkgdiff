using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Validation;
using Polly;
using Polly.Retry;
using System.IO.Compression;
using System.Xml.Linq;
using System.Collections.Concurrent;

namespace PkgDiff.Infrastructure.Services;

public class ScraperService : IScraperService
{
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly IRateLimiterService _rateLimiter;
    private readonly ILogger<ScraperService> _logger;

    // Request deduplication: prevent multiple concurrent requests for the same resource
    private readonly ConcurrentDictionary<string, Task<string>> _releaseNotesCache = new();
    private readonly ConcurrentDictionary<string, Task<string?>> _githubUrlCache = new();

    private readonly IHtmlDocumentLoader _htmlDocumentLoader;
    private readonly IHttpClientFactory _httpClientFactory;

    public ScraperService(
        IRateLimiterService rateLimiter,
        ILogger<ScraperService> logger,
        IHtmlDocumentLoader htmlDocumentLoader,
        IHttpClientFactory httpClientFactory)
    {
        _rateLimiter = rateLimiter;
        _logger = logger;
        _htmlDocumentLoader = htmlDocumentLoader;
        _httpClientFactory = httpClientFactory;

        // Polly retry policy: 3 attempts with exponential backoff, for transient failures only
        _retryPolicy = Policy
            .Handle<Exception>(TransientHttp.IsTransient)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<string> GetReleaseNotesAsync(string packageId, string version)
    {
        var cacheKey = $"{packageId}:{version}";
        
        // Request deduplication: if there's already a pending request for this package+version, reuse it
        return await _releaseNotesCache.GetOrAdd(cacheKey, async key =>
        {
            try
            {
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Use rate limiter to respect NuGet.org's 50 req/min limit
                    return await _rateLimiter.ExecuteAsync("nuget-scraping", async () =>
                    {
                        var url = $"https://www.nuget.org/packages/{PackageUrlEncoder.EscapeSegment(packageId)}/{PackageUrlEncoder.EscapeSegment(version)}";
                        _logger.LogDebug("[ScraperService] Fetching release notes from: {Url}", url);
                        using var document = await _htmlDocumentLoader.LoadAsync(url);
                        if (document == null)
                        {
                            _logger.LogWarning("[ScraperService] No release notes found for {PackageId} {Version}", packageId, version);
                            return string.Empty;
                        }

                        // Try multiple selectors as fallback
                        // NOTE: Use #releasenotes-tab (content div), NOT #releasenotes-body-tab (tab button)
                        var selectors = new[]
                        {
                            "#releasenotes-tab p",
                            "#releasenotes-tab"
                        };

                        foreach (var selector in selectors)
                        {
                            var element = document.QuerySelector(selector);
                            if (element != null && !string.IsNullOrWhiteSpace(element.TextContent))
                            {
                                var cleaned = CleanReleaseNotes(element.TextContent);
                                _logger.LogDebug("[ScraperService] Found release notes with selector '{Selector}': {Preview}...",
                                    selector, cleaned[..Math.Min(100, cleaned.Length)]);
                                return cleaned;
                            }
                        }

                        _logger.LogWarning("[ScraperService] No release notes found for {PackageId} {Version}", packageId, version);
                        return string.Empty;
                    });
                });
            }
            finally
            {
                // Remove from cache after completion to allow fresh requests later
                _releaseNotesCache.TryRemove(cacheKey, out _);
            }
        });
    }

    public async Task<string?> GetGitHubRepositoryUrlAsync(string packageId)
    {
        // Request deduplication: if there's already a pending request for this package, reuse it
        return await _githubUrlCache.GetOrAdd(packageId, async key =>
        {
            try
            {
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Use rate limiter to respect NuGet.org's 50 req/min limit
                    return await _rateLimiter.ExecuteAsync("nuget-scraping", async () =>
                    {
                        // Try HTML scraping first (faster and simpler)
                        var githubUrl = await ScrapeGitHubUrlFromHtmlAsync(packageId);
                        if (!string.IsNullOrWhiteSpace(githubUrl))
                        {
                            return CleanGitHubUrl(githubUrl);
                        }

                        _logger.LogWarning("[ScraperService] HTML scraping failed for {PackageId}, no .nupkg fallback implemented", packageId);
                        return null;
                    });
                });
            }
            finally
            {
                // Remove from cache after completion to allow fresh requests later
                _githubUrlCache.TryRemove(packageId, out _);
            }
        });
    }

    private async Task<string?> GetLatestVersionAsync(string packageId)
    {
        try
        {
            // Use NuGet V3 API to get package metadata
            var url = $"https://api.nuget.org/v3/registration5-gz-semver2/{PackageUrlEncoder.EscapeSegment(packageId.ToLowerInvariant())}/index.json";

            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            // Get the last page (contains latest versions)
            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0)
                return null;

            var lastPage = items[items.GetArrayLength() - 1];
            
            // Get items from the page
            var pageItems = lastPage.GetProperty("items");
            if (pageItems.GetArrayLength() == 0)
                return null;

            // Get the last item (latest version)
            var latestItem = pageItems[pageItems.GetArrayLength() - 1];
            var catalogEntry = latestItem.GetProperty("catalogEntry");
            var version = catalogEntry.GetProperty("version").GetString();

            return version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ScraperService] Could not read the latest version from the NuGet registration index");
            return null;
        }
    }

    /// <summary>
    /// Extracts GitHub repository URL from .nuspec XML content.
    /// </summary>
    private string? ExtractGitHubUrlFromNuspec(string nuspecContent)
    {
        try
        {
            var xmlDoc = XDocument.Parse(nuspecContent);
            var ns = xmlDoc.Root?.GetDefaultNamespace();

            if (ns != null)
            {
                // First, try <repository> tag (most specific)
                var repositoryElement = xmlDoc.Descendants(ns + "repository").FirstOrDefault();
                var url = repositoryElement?.Attribute("url")?.Value;

                if (GitHubUrls.IsGitHub(url))
                {
                    _logger.LogDebug("[ScraperService] Found GitHub URL in <repository> tag: {Url}", url);
                    return url;
                }

                // Fallback: Try <projectUrl> tag (some packages put GitHub URL here)
                var projectUrlElement = xmlDoc.Descendants(ns + "projectUrl").FirstOrDefault();
                var projectUrl = projectUrlElement?.Value;

                if (GitHubUrls.IsGitHub(projectUrl))
                {
                    _logger.LogDebug("[ScraperService] Found GitHub URL in <projectUrl> tag: {Url}", projectUrl);
                    return projectUrl;
                }
            }

            return null;
        }
        catch (System.Xml.XmlException ex)
        {
            _logger.LogDebug(ex, "[ScraperService] The nuspec is not valid XML");
            return null;
        }
    }

    private string CleanGitHubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        url = url.TrimEnd('/');

        // Remove common GitHub paths to get base repository URL
        var segments = new[] { "/issues", "/wiki", "/pulls", "/actions", "/tree", "/blob", "/commit" };
        foreach (var segment in segments)
        {
            var index = url.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                url = url.Substring(0, index);
                break;
            }
        }

        return url;
    }

    private string CleanReleaseNotes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove excessive whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        
        // Trim
        text = text.Trim();

        return text;
    }

    // ==================== HELPER METHODS ====================

    /// <summary>
    /// Scrapes GitHub repository URL from NuGet package HTML page.
    /// </summary>
    private async Task<string?> ScrapeGitHubUrlFromHtmlAsync(string packageId)
    {
        try
        {
            var url = $"https://www.nuget.org/packages/{PackageUrlEncoder.EscapeSegment(packageId)}";
            using var document = await _htmlDocumentLoader.LoadAsync(url);
            if (document == null)
            {
                return null;
            }

            // Try the specific selector from the actual HTML
            var githubLink = document.QuerySelector("a[data-track='outbound-repository-url'][href*='github.com']");

            if (githubLink != null)
            {
                var href = githubLink.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    _logger.LogDebug("[ScraperService] Found GitHub URL via HTML: {Url}", href);
                    return href;
                }
            }

            // Fallback 1: Try any link with "Source repository" text
            var links = document.QuerySelectorAll("a[href*='github.com']");
            foreach (var link in links)
            {
                var text = link.TextContent?.Trim();
                if (text != null && text.Contains("Source repository", StringComparison.OrdinalIgnoreCase))
                {
                    var href = link.GetAttribute("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        _logger.LogDebug("[ScraperService] Found GitHub URL via 'Source repository' text: {Url}", href);
                        return href;
                    }
                }
            }

            // Fallback 2: Try "Project website" link (some packages put GitHub URL here)
            foreach (var link in links)
            {
                var text = link.TextContent?.Trim();
                if (text != null && text.Contains("Project website", StringComparison.OrdinalIgnoreCase))
                {
                    var href = link.GetAttribute("href");
                    if (GitHubUrls.IsGitHub(href))
                    {
                        _logger.LogDebug("[ScraperService] Found GitHub URL via 'Project website' text: {Url}", href);
                        return href;
                    }
                }
            }

            // Developer note: if HTML needs to be inspected during dev, handle it securely here.

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScraperService] HTML scraping error for {PackageId}", packageId);
            return null;
        }
    }
}
