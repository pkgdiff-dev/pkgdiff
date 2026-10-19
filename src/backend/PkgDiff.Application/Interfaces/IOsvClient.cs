using PkgDiff.Application.Security;

namespace PkgDiff.Application.Interfaces;

public interface IOsvClient
{
    Task<OsvReport> QueryAsync(string registry, string packageId, IReadOnlyList<string> versions, CancellationToken cancellationToken = default);

    // One version each of many packages, as a dependency audit asks; in the order given
    Task<IReadOnlyList<OsvPackageResult>> QueryPackagesAsync(
        string registry,
        IReadOnlyList<(string Name, string Version)> packages,
        CancellationToken cancellationToken = default);
}
