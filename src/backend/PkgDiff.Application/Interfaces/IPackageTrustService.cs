using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IPackageTrustService
{
    // Reads the stored data only and queues a refresh when it is missing or old; never calls a provider
    Task<PackageTrust> GetTrustAsync(string registry, string packageId);

    Task RefreshAsync(string registry, string packageId);

    Task RefreshExpiredAsync();
}
