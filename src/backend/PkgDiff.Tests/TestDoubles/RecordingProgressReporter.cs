using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Tests.TestDoubles;

public sealed class RecordingProgressReporter : IProgressReporter
{
    public List<LiveUpdate> LiveUpdates { get; } = new();

    public Exception? BroadcastException { get; set; }

    public Task SendProgressAsync(string? connectionId, string message) => Task.CompletedTask;

    public Task BroadcastLiveUpdateAsync(LiveUpdate update)
    {
        if (BroadcastException != null)
            throw BroadcastException;

        LiveUpdates.Add(update);
        return Task.CompletedTask;
    }

    public Task BroadcastPackageUpdatedAsync(string packageId) => Task.CompletedTask;

    public Task BroadcastPackageUpdateStartedAsync(string packageId) => Task.CompletedTask;

    public Task BroadcastBackgroundProgressAsync(string packageId, string message) => Task.CompletedTask;

    public Task BroadcastSecurityUpdatedAsync(string packageId) => Task.CompletedTask;
}
