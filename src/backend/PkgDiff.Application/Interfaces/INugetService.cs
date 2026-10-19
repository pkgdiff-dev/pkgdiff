using NuGet.Versioning;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface INugetService
{
    Task<IEnumerable<NuGetVersion>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false);
    Task<IEnumerable<NuGetVersion>> GetVersionsInRangeAsync(string packageId, NuGetVersion fromVersion, NuGetVersion toVersion, bool includePrerelease = false);
    Task<bool> PackageExistsAsync(string packageId);
    Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version);
    Task<PackageMetadata> GetPackageMetadataAsync(string packageId);
}
