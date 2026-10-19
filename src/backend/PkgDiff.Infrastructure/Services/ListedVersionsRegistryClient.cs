using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

// A registry that lists its versions as text: parsed by its scheme, then cut by the exclude patterns
public abstract class ListedVersionsRegistryClient : IRegistryClient
{
    private readonly IVersionFilterService _versionFilterService;
    private readonly ILogger _logger;

    protected ListedVersionsRegistryClient(IVersionFilterService versionFilterService, ILogger logger)
    {
        _versionFilterService = versionFilterService;
        _logger = logger;
    }

    public abstract string Id { get; }

    public abstract string SourceLabel { get; }

    public abstract VersionScheme VersionScheme { get; }

    public bool RangeIncludesStartingVersion => true;

    public abstract bool PublishesReleaseNotes { get; }

    public virtual bool KnowsSourceDirectory => false;

    protected abstract Task<IEnumerable<string>> ListVersionsAsync(string packageId, bool includePrerelease);

    public async Task<bool> PackageExistsAsync(string packageId, bool includePrerelease) =>
        (await ListVersionsAsync(packageId, includePrerelease)).Any();

    public async Task<IReadOnlyList<PackageVersion>> GetVersionsAsync(string packageId, bool includePrerelease)
    {
        var listed = await ListVersionsAsync(packageId, includePrerelease);
        var versions = PackageVersions.ParseAll(VersionScheme, listed, includePrerelease, out var skipped);
        if (skipped.Count > 0)
        {
            _logger.LogWarning(
                "Skipped {Count} versions of {PackageId} on {Registry} that cannot be parsed: {Versions}",
                skipped.Count, packageId, Id, string.Join(", ", skipped.Take(10)));
        }

        var excludePatterns = await _versionFilterService.GetActiveExcludePatternsAsync();
        return versions
            .Where(v => !_versionFilterService.ShouldExcludeVersion(v.Value, excludePatterns))
            .ToList();
    }

    public abstract Task<DateTime?> GetPublishedDateAsync(string packageId, string version);

    // The version list of the package page is what the background scrape walks, so it is cut by the
    // same exclude patterns as the versions of an analysis
    public async Task<PackageMetadata> GetMetadataAsync(string packageId)
    {
        var metadata = await FetchMetadataAsync(packageId);
        var excludePatterns = await _versionFilterService.GetActiveExcludePatternsAsync();
        metadata.Versions = metadata.Versions
            .Where(v => !_versionFilterService.ShouldExcludeVersion(v.Version, excludePatterns))
            .ToList();
        return metadata;
    }

    protected abstract Task<PackageMetadata> FetchMetadataAsync(string packageId);

    public abstract Task<string?> GetOwnReleaseNotesAsync(string packageId, string version);

    public abstract IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl);
}
