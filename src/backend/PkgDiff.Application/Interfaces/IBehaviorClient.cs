using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IBehaviorClient
{
    // Version -> report for the versions the provider knows; empty when it is switched off
    Task<Dictionary<string, BehaviorReport>> GetReportsAsync(
        string registry,
        string packageId,
        IReadOnlyList<string> versions,
        CancellationToken cancellationToken = default);
}
