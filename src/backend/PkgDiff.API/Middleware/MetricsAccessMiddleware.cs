using PkgDiff.API.Options;

namespace PkgDiff.API.Middleware;

public class MetricsAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TrustedNetworks _trustedNetworks;

    public MetricsAccessMiddleware(RequestDelegate next, TrustedNetworks trustedNetworks)
    {
        _next = next;
        _trustedNetworks = trustedNetworks;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/metrics") && !IsInternalScrape(context))
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!context.User.IsInRole("Admin"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await _next(context);
    }

    // X-Original-For means a proxied visitor; the bridge gateway carries host-originated traffic
    private bool IsInternalScrape(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress;
        return peer is not null
               && !context.Request.Headers.ContainsKey("X-Original-For")
               && _trustedNetworks.Contains(peer)
               && !_trustedNetworks.IsGateway(peer);
    }
}
