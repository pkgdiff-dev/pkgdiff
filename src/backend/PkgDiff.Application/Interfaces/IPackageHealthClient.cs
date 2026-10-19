using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IPackageHealthClient
{
    // null when neither deps.dev nor the Scorecard API knows the package or its repository
    Task<PackageHealth?> GetHealthAsync(
        string registry,
        string packageId,
        string? version,
        string? repositoryUrl,
        CancellationToken cancellationToken = default);
}
