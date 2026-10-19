using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

// GitHub releases first, then the notes the registry itself publishes, when it publishes any.
// Used for registries whose notes only come from a repository and for NuGet packages with a known repository.
public class GitHubFirstScrapingStrategy : IReleaseNotesScrapingStrategy
{
    private readonly IGitHubScraperService _githubScraperService;
    private readonly IReleaseNotesUrlResolver _urlResolver;
    private readonly IScrapeLogService _scrapeLogService;
    private readonly ILogger<GitHubFirstScrapingStrategy> _logger;

    // The folder of each package, read once per scope however many versions are analyzed
    private readonly ConcurrentDictionary<string, Task<string?>> _sourceDirectories = new();

    public GitHubFirstScrapingStrategy(
        IGitHubScraperService githubScraperService,
        IReleaseNotesUrlResolver urlResolver,
        IScrapeLogService scrapeLogService,
        ILogger<GitHubFirstScrapingStrategy> logger)
    {
        _githubScraperService = githubScraperService;
        _urlResolver = urlResolver;
        _scrapeLogService = scrapeLogService;
        _logger = logger;
    }

    public async Task<ReleaseNotesResult> GetReleaseNotesAsync(
        string packageId, string version, string? githubRepoUrl, IRegistryClient registry)
    {
        _logger.LogDebug("[GitHubFirstStrategy] Starting for {PackageId} v{Version}, githubRepoUrl={GitHubUrl}",
            packageId, version, githubRepoUrl ?? "(null)");

        string? discoveredUrl = null;

        // If no GitHub URL yet, try to discover from registry
        if (string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _logger.LogDebug("[GitHubFirstStrategy] No GitHub URL, trying registry lookup for {PackageId} (Registry: {Registry})", packageId, registry.Id);
            try
            {
                githubRepoUrl = (await registry.GetMetadataAsync(packageId)).GitHubRepositoryUrl;

                if (!string.IsNullOrWhiteSpace(githubRepoUrl))
                {
                    discoveredUrl = githubRepoUrl;
                    _logger.LogInformation("[GitHubFirstStrategy] Discovered GitHub URL from {Registry}: {GitHubUrl}", registry.Id, githubRepoUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[GitHubFirstStrategy] Registry lookup failed for {PackageId}", packageId);
            }
        }

        // Try GitHub first
        if (!string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _logger.LogDebug("[GitHubFirstStrategy] Trying GitHub releases for {PackageId} v{Version} from {GitHubUrl}",
                packageId, version, githubRepoUrl);
            // A monorepo package keeps its changelog in its own folder; the URL tells the scraper where
            var lookupUrl = githubRepoUrl;
            if (GitHubUrls.SubPath(githubRepoUrl) is null)
            {
                var directory = await SourceDirectoryAsync(packageId, registry);
                if (!string.IsNullOrEmpty(directory))
                    lookupUrl = $"{GitHubUrls.Clean(githubRepoUrl)}/tree/HEAD/{directory}";
            }

            var notes = await _githubScraperService.GetReleaseNotesForVersionAsync(lookupUrl, version, packageId);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                _logger.LogDebug("[GitHubFirstStrategy] SUCCESS - Got release notes from GitHub for {PackageId} v{Version} (length: {Length})",
                    packageId, version, notes.Length);
                return new ReleaseNotesResult(notes, "GitHub", discoveredUrl);
            }
            _logger.LogWarning("[GitHubFirstStrategy] GitHub returned empty for {PackageId} v{Version} (registry fallback: {HasFallback})",
                packageId, version, registry.PublishesReleaseNotes);
        }

        // Fall back to the registry's own notes, when it publishes any
        if (registry.PublishesReleaseNotes)
        {
            _logger.LogDebug("[GitHubFirstStrategy] Trying {Registry} notes for {PackageId} v{Version}", registry.Id, packageId, version);
            var ownNotes = await registry.GetOwnReleaseNotesAsync(packageId, version);
            if (!string.IsNullOrWhiteSpace(ownNotes))
            {
                _logger.LogDebug("[GitHubFirstStrategy] {Registry} returned notes for {PackageId} v{Version} (length: {Length}): {Preview}...",
                    registry.Id, packageId, version, ownNotes.Length, ownNotes[..Math.Min(150, ownNotes.Length)]);

                // Check if the registry's notes are URL-dominated and try to resolve actual content
                var resolved = await _urlResolver.ResolveAsync(ownNotes, packageId, version, githubRepoUrl);
                if (resolved.WasResolved)
                {
                    _logger.LogDebug("[GitHubFirstStrategy] URL resolved for {PackageId} v{Version}, source: {Source}",
                        packageId, version, resolved.ResolvedSource);
                    return new ReleaseNotesResult(
                        resolved.ReleaseNotes,
                        resolved.ResolvedSource ?? registry.SourceLabel,
                        resolved.DiscoveredGitHubUrl ?? discoveredUrl);
                }

                return new ReleaseNotesResult(ownNotes, registry.SourceLabel, discoveredUrl);
            }
        }

        _logger.LogWarning("[GitHubFirstStrategy] ALL SOURCES EMPTY for {PackageId} v{Version}", packageId, version);

        // Persist failure to scrape logs for admin review
        var allReleases = !string.IsNullOrWhiteSpace(githubRepoUrl)
            ? await _githubScraperService.GetAllReleasesAsync(githubRepoUrl)
            : new Dictionary<string, string>();

        await _scrapeLogService.LogFailureAsync(
            packageId, version, registry.Id, githubRepoUrl,
            failureReason: !string.IsNullOrWhiteSpace(githubRepoUrl)
                ? $"GitHub version mismatch - no release matching '{version}'"
                : $"No GitHub URL and {registry.Id} registry has no fallback",
            availableVersions: allReleases.Count > 0
                ? string.Join(", ", allReleases.Keys.Take(30))
                : null,
            attemptedSources: registry.PublishesReleaseNotes ? $"GitHub, {registry.SourceLabel}" : "GitHub");

        return new ReleaseNotesResult(string.Empty, "GitHub", discoveredUrl);
    }

    private Task<string?> SourceDirectoryAsync(string packageId, IRegistryClient registry)
    {
        if (!registry.KnowsSourceDirectory)
            return Task.FromResult<string?>(null);

        return _sourceDirectories.GetOrAdd($"{registry.Id}:{packageId}", async _ =>
        {
            try
            {
                return (await registry.GetMetadataAsync(packageId)).SourceDirectory;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[GitHubFirstStrategy] Could not read the source folder of {PackageId}", packageId);
                return null;
            }
        });
    }
}
