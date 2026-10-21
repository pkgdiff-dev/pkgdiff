using PkgDiff.Domain.Entities;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IPackageCacheService
{
    Task<CachedPackage?> GetCachedPackageAsync(string packageId, string registry);
    Task<CachedPackage> GetOrCreateCachedPackageAsync(string packageId, string registry, string latestVersion, IEnumerable<string> versionStrings);
    Task UpdateCachedPackageAsync(CachedPackage cachedPackage);

    // A visitor opened or compared the package
    Task MarkViewedAsync(string registry, string packageId);
    Task<CachedReleaseNote?> GetCachedReleaseNoteAsync(int cachedPackageId, string version);
    Task SaveReleaseNoteAsync(CachedReleaseNote releaseNote);
    Task<PackageMetadata> GetOrFetchMetadataAsync(string registry, string packageId, bool bypassCache = false);
    Task<int> ResolveRegistryIdAsync(string registryValue);

    // Null for a repository that is private, missing or not a repository
    Task<string?> UsableRepositoryUrlAsync(string? repositoryUrl);
}
