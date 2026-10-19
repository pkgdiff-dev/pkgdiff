using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IProgressReporter
{
    Task SendProgressAsync(string? connectionId, string message);
    Task BroadcastLiveUpdateAsync(LiveUpdate update);
    Task BroadcastPackageUpdatedAsync(string packageId);
    Task BroadcastPackageUpdateStartedAsync(string packageId);
    Task BroadcastBackgroundProgressAsync(string packageId, string message);
    Task BroadcastSecurityUpdatedAsync(string packageId);
}
