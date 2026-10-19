using PkgDiff.Domain.Entities;
using System.Diagnostics;
using PkgDiff.API.Extensions;
using System.Threading.Channels;

namespace PkgDiff.API.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    // Endpoint prefixes that belong to internal/system traffic — never logged.
    // Skipping is path-only: behind the proxy every peer is a 172.* address, so IP or
    // user-agent based skips would silently drop real visitor traffic.
    private static readonly string[] _skipEndpoints =
    [
        "/health", "/metrics", "/swagger", "/hangfire",
        "/analysisHub", "/favicon", "/_framework",
    ];

    // File extensions that are static assets — never logged
    private static readonly string[] _skipExtensions =
    [
        ".js", ".css", ".ico", ".png", ".jpg", ".jpeg",
        ".gif", ".svg", ".woff", ".woff2", ".ttf", ".map",
    ];

    public RequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, Channel<RequestLog> logChannel)
    {
        var ipAddress = context.GetClientIpAddress();
        var endpoint = context.Request.Path.ToString();
        var method = context.Request.Method;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            if (!ShouldSkip(endpoint))
            {
                var requestLog = new RequestLog
                {
                    IpAddress = ipAddress,
                    Endpoint = endpoint,
                    Method = method,
                    StatusCode = context.Response.StatusCode,
                    UserAgent = userAgent,
                    CreatedAt = DateTime.UtcNow
                };

                await logChannel.Writer.WriteAsync(requestLog);
            }
        }
    }

    public static bool ShouldSkip(string path)
    {
        foreach (var skip in _skipEndpoints)
            if (path.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
                return true;

        foreach (var ext in _skipExtensions)
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
