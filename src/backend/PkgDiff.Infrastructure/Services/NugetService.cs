using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class NugetService : INugetService
{
    private readonly SourceRepository _repository;
    private readonly SourceCacheContext _cache;
    private readonly IVersionFilterService _versionFilterService;
    private readonly ILogger<NugetService> _logger;

    public NugetService(IVersionFilterService versionFilterService, ILogger<NugetService> logger)
    {
        var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
        _repository = Repository.Factory.GetCoreV3(packageSource);
        _cache = new SourceCacheContext();
        _versionFilterService = versionFilterService;
        _logger = logger;
    }

    private async Task<PackageMetadataResource> GetMetadataResourceAsync() =>
        await _repository.GetResourceAsync<PackageMetadataResource>()
        ?? throw new InvalidOperationException("NuGet package metadata resource is unavailable.");

    public async Task<IEnumerable<NuGetVersion>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false)
    {
        var metadataResource = await GetMetadataResourceAsync();
        var metadata = await metadataResource.GetMetadataAsync(
            packageId,
            includePrerelease: true, // Always fetch all versions, filter below
            includeUnlisted: false,
            _cache,
            NullLogger.Instance,
            WorkCancellation.Token);

        var versions = metadata.Select(m => m.Identity.Version);

        // Get active exclude patterns
        var excludePatterns = await _versionFilterService.GetActiveExcludePatternsAsync();

        // Filter out prerelease versions if not requested
        if (!includePrerelease)
        {
            versions = versions
                .Where(v =>
                    !v.IsPrerelease &&
                    !_versionFilterService.ShouldExcludeVersion(v.ToString(), excludePatterns));
        }
        else
        {
            // Even when including prerelease, filter out excluded patterns (dev, ci, etc.)
            versions = versions
                .Where(v => !_versionFilterService.ShouldExcludeVersion(v.ToString(), excludePatterns));
        }

        return versions.OrderBy(v => v).ToList();
    }

    public async Task<IEnumerable<NuGetVersion>> GetVersionsInRangeAsync(
        string packageId, 
        NuGetVersion fromVersion, 
        NuGetVersion toVersion, 
        bool includePrerelease = false)
    {
        var allVersions = await GetPackageVersionsAsync(packageId, includePrerelease);
        
        return allVersions
            .Where(v => v > fromVersion && v <= toVersion)
            .OrderBy(v => v)
            .ToList();
    }

    public async Task<bool> PackageExistsAsync(string packageId)
    {
        try
        {
            var versions = await GetPackageVersionsAsync(packageId, includePrerelease: true);
            return versions.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NuGet lookup failed for {PackageId}", packageId);
            return false;
        }
    }

    public async Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version)
    {
        try
        {
            var metadataResource = await GetMetadataResourceAsync();
            var metadata = await metadataResource.GetMetadataAsync(
                packageId,
                includePrerelease: true,
                includeUnlisted: false,
                _cache,
                NullLogger.Instance,
                WorkCancellation.Token);

            var packageMetadata = metadata.FirstOrDefault(m => m.Identity.Version.ToString() == version);
            return packageMetadata?.Published?.DateTime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NuGet publish date lookup failed for {PackageId} {Version}", packageId, version);
            return null;
        }
    }
    
    public async Task<PackageMetadata> GetPackageMetadataAsync(string packageId)
    {
        var metadataResource = await GetMetadataResourceAsync();
        var metadata = (await metadataResource.GetMetadataAsync(
            packageId,
            includePrerelease: true,
            includeUnlisted: false,
            _cache,
            NullLogger.Instance,
            WorkCancellation.Token)).ToList();

        if (!metadata.Any())
        {
            throw new ArgumentException($"Package '{packageId}' not found on NuGet.");
        }

        // Get active exclude patterns
        var excludePatterns = await _versionFilterService.GetActiveExcludePatternsAsync();

        _logger.LogDebug("[NugetService] Fetched {TotalCount} versions for {PackageId}", 
            metadata.Count, packageId);

        // Filter out excluded versions (dev, ci, etc.)
        var filteredMetadata = metadata
            .Where(m => !_versionFilterService.ShouldExcludeVersion(m.Identity.Version.ToString(), excludePatterns))
            .ToList();

        _logger.LogDebug("[NugetService] After filtering: {FilteredCount}/{TotalCount} versions remain for {PackageId}", 
            filteredMetadata.Count, metadata.Count, packageId);

        // Use filtered metadata for latest version
        var latest = filteredMetadata.OrderByDescending(m => m.Identity.Version).First();

        return new PackageMetadata
        {
            PackageId = packageId,
            LatestVersion = latest.Identity.Version.ToString(),
            Description = latest.Description,
            Authors = latest.Authors,
            IconUrl = latest.IconUrl?.ToString(),
            ProjectUrl = latest.ProjectUrl?.ToString(),
            Versions = filteredMetadata
                .Select(m => new { m.Identity.Version, m.Published })
                .DistinctBy(x => x.Version.ToString())
                .OrderByDescending(x => x.Version)
                .Select(x => new VersionSummary
                {
                    Version = x.Version.ToString(),
                    PublishedDate = x.Published?.DateTime,
                    IsPrerelease = x.Version.IsPrerelease,
                })
                .ToList()
        };
    }
}
