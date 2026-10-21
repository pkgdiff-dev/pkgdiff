using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IMavenService
{
    Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false);
    Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version);
    Task<PackageMetadata> GetPackageMetadataAsync(string packageId);
}
