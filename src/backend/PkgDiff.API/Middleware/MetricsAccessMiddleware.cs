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

    // ForwardedHeadersMiddleware records the original peer only when it applied X-Forwarded-For, so
    // that header's presence means the request came through the proxy chain from a visitor. The bridge gateway is
    // where host-originated traffic (docker-proxy) appears from, so it is not an internal container.
    private bool IsInternalScrape(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress;
        return peer is not null
               && !context.Request.Headers.ContainsKey("X-Original-For")
               && _trustedNetworks.Contains(peer)
               && !_trustedNetworks.IsGateway(peer);
    }
}
