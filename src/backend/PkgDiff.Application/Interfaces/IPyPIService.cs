using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IPyPIService
{
    Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false);
    Task<IEnumerable<string>> GetVersionsInRangeAsync(string packageId, string fromVersion, string toVersion, bool includePrerelease = false);
    Task<bool> PackageExistsAsync(string packageId);
    Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version);
    Task<PackageMetadata> GetPackageMetadataAsync(string packageId);
}
