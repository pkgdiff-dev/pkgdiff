using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using PkgDiff.API.Services;

namespace PkgDiff.API.Hubs;

public class AnalysisHub : Hub
{
    private readonly IDataProtectionProvider _dataProtection;

    public AnalysisHub(IDataProtectionProvider dataProtection)
    {
        _dataProtection = dataProtection;
    }

    public async Task SendProgress(string message)
    {
        await Clients.Caller.SendAsync("ReceiveProgress", message);
    }

    public string GetProgressToken() => ProgressTokens.Create(_dataProtection, Context.ConnectionId);
}
