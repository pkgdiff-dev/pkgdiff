using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PkgDiff.API.Hubs;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Services;

public class SignalrProgressReporter : IProgressReporter
{
    private readonly IHubContext<AnalysisHub> _hubContext;
    private readonly ILogger<SignalrProgressReporter> _logger;

    public SignalrProgressReporter(IHubContext<AnalysisHub> hubContext, ILogger<SignalrProgressReporter> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendProgressAsync(string? connectionId, string message)
    {
        if (string.IsNullOrEmpty(connectionId)) return;
        await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveProgress", message);
    }

    public async Task BroadcastBackgroundProgressAsync(string packageId, string message)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveBackgroundProgress", new { PackageId = packageId, Message = message });
    }

    public async Task BroadcastLiveUpdateAsync(LiveUpdate update)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveLiveUpdate", update);
    }

    public async Task BroadcastPackageUpdatedAsync(string packageId)
    {
        _logger.LogDebug("[SignalR] Broadcasting ReceivePackageUpdated for: {PackageId}", packageId);
        await _hubContext.Clients.All.SendAsync("ReceivePackageUpdated", packageId);
    }

    public async Task BroadcastSecurityUpdatedAsync(string packageId)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveSecurityUpdated", packageId);
    }

    public async Task BroadcastPackageUpdateStartedAsync(string packageId)
    {
        _logger.LogDebug("[SignalR] Broadcasting ReceivePackageUpdateStarted for: {PackageId}", packageId);
        await _hubContext.Clients.All.SendAsync("ReceivePackageUpdateStarted", packageId);
    }
}
