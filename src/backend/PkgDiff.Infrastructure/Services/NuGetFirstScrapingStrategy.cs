using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

// The NuGet package page first, then GitHub releases
public class NuGetFirstScrapingStrategy : IReleaseNotesScrapingStrategy
{
    private readonly IScraperService _scraperService;
    private readonly IGitHubScraperService _githubScraperService;
    private readonly IReleaseNotesUrlResolver _urlResolver;
    private readonly IScrapeLogService _scrapeLogService;
    private readonly ILogger<NuGetFirstScrapingStrategy> _logger;

    public NuGetFirstScrapingStrategy(
        IScraperService scraperService,
        IGitHubScraperService githubScraperService,
        IReleaseNotesUrlResolver urlResolver,
        IScrapeLogService scrapeLogService,
        ILogger<NuGetFirstScrapingStrategy> logger)
    {
        _scraperService = scraperService;
        _githubScraperService = githubScraperService;
        _urlResolver = urlResolver;
        _scrapeLogService = scrapeLogService;
        _logger = logger;
    }

    public async Task<ReleaseNotesResult> GetReleaseNotesAsync(
        string packageId, string version, string? githubRepoUrl, IRegistryClient registry)
    {
        _logger.LogDebug("[NuGetFirstStrategy] Starting for {PackageId} v{Version}, githubRepoUrl={GitHubUrl}",
            packageId, version, githubRepoUrl ?? "(null)");

        string? discoveredUrl = null;

        _logger.LogDebug("[NuGetFirstStrategy] Trying NuGet scraping for {PackageId} v{Version}", packageId, version);
        var releaseNotes = await _scraperService.GetReleaseNotesAsync(packageId, version);
        if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            _logger.LogDebug("[NuGetFirstStrategy] NuGet returned notes for {PackageId} v{Version} (length: {Length}): {Preview}...",
                packageId, version, releaseNotes.Length, releaseNotes[..Math.Min(150, releaseNotes.Length)]);

            var resolved = await _urlResolver.ResolveAsync(releaseNotes, packageId, version, githubRepoUrl);
            if (resolved.WasResolved)
            {
                _logger.LogDebug("[NuGetFirstStrategy] URL resolved for {PackageId} v{Version}, source: {Source}",
                    packageId, version, resolved.ResolvedSource);
                return new ReleaseNotesResult(
                    resolved.ReleaseNotes,
                    resolved.ResolvedSource ?? "NuGet",
                    resolved.DiscoveredGitHubUrl);
            }

            return new ReleaseNotesResult(releaseNotes, "NuGet", null);
        }

        _logger.LogWarning("[NuGetFirstStrategy] NuGet returned empty for {PackageId} v{Version}", packageId, version);

        if (string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _logger.LogDebug("[NuGetFirstStrategy] Discovering GitHub URL for {PackageId}", packageId);
            githubRepoUrl = await _scraperService.GetGitHubRepositoryUrlAsync(packageId);
            if (!string.IsNullOrWhiteSpace(githubRepoUrl))
            {
                discoveredUrl = githubRepoUrl;
                _logger.LogInformation("[NuGetFirstStrategy] Discovered GitHub URL: {GitHubUrl}", githubRepoUrl);
            }
            else
            {
                _logger.LogWarning("[NuGetFirstStrategy] Could not discover GitHub URL for {PackageId}", packageId);
            }
        }

        if (!string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _logger.LogDebug("[NuGetFirstStrategy] Trying GitHub fallback for {PackageId} v{Version} from {GitHubUrl}",
                packageId, version, githubRepoUrl);
            var githubNotes = await _githubScraperService.GetReleaseNotesForVersionAsync(githubRepoUrl, version, packageId);
            if (!string.IsNullOrWhiteSpace(githubNotes))
            {
                _logger.LogDebug("[NuGetFirstStrategy] SUCCESS - Got release notes from GitHub for {PackageId} v{Version} (length: {Length})",
                    packageId, version, githubNotes.Length);
                return new ReleaseNotesResult(githubNotes, "GitHub", discoveredUrl);
            }
            _logger.LogWarning("[NuGetFirstStrategy] GitHub also returned empty for {PackageId} v{Version}", packageId, version);
        }

        _logger.LogWarning("[NuGetFirstStrategy] ALL SOURCES EMPTY for {PackageId} v{Version}", packageId, version);

        var attemptedSources = !string.IsNullOrWhiteSpace(githubRepoUrl) ? "NuGet, GitHub" : "NuGet";
        string? availableVersions = null;

        if (!string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            var allReleases = await _githubScraperService.GetAllReleasesAsync(githubRepoUrl);
            if (allReleases.Count > 0)
                availableVersions = string.Join(", ", allReleases.Keys.Take(30));
        }

        await _scrapeLogService.LogFailureAsync(
            packageId, version, registry.Id, githubRepoUrl,
            failureReason: !string.IsNullOrWhiteSpace(githubRepoUrl)
                ? $"GitHub version mismatch - no release matching '{version}'"
                : "NuGet page has no release notes and no GitHub repo found",
            availableVersions: availableVersions,
            attemptedSources: attemptedSources);

        return new ReleaseNotesResult(string.Empty, "NuGet", discoveredUrl);
    }
}
