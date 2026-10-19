using Microsoft.Extensions.Options;
using PkgDiff.API.Extensions;
using PkgDiff.API.Options;
using PkgDiff.API.Services;
using System.Net;

namespace PkgDiff.API.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitCounterStore _counters;
    private readonly InboundRateLimitOptions _options;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IRateLimitCounterStore counters,
        IOptions<InboundRateLimitOptions> options)
    {
        _next = next;
        _counters = counters;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ip = context.GetClientIpAddress();
        var path = context.Request.Path;

        // Runs before authentication: only callers with credentials get the admin budget
        if (IsAdminPath(path) && HasCredentials(context))
        {
            if (await IsOverLimitAsync("rl:admin", ip, _options.Admin))
            {
                await ReturnRateLimitError(context, "Too many requests to admin endpoints. Please slow down.");
                return;
            }
        }
        else if (await IsOverLimitAsync("rl", ip, _options.General))
        {
            await ReturnRateLimitError(context, "Too many requests. Please slow down.");
            return;
        }
        // Separate counters: the page starting an export already spends the general budget
        else if (IsPdfExport(path) && await IsOverLimitAsync("rl:pdf", ip, _options.PdfExport))
        {
            await ReturnRateLimitError(context, "Too many requests for PDF exports. Please slow down.");
            return;
        }
        else if (IsPost(context, "/api/auth/login") && await IsOverLimitAsync("rl:login", ip, _options.Login))
        {
            await ReturnRateLimitError(context, "Too many sign-in attempts. Please wait a few minutes.");
            return;
        }
        else if (IsPost(context, "/api/audit") && await IsOverLimitAsync("rl:audit", ip, _options.AuditStart))
        {
            await ReturnRateLimitError(context, "Too many audits started. Please wait a few minutes.");
            return;
        }

        await _next(context);
    }

    private static bool IsAdminPath(PathString path) =>
        path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/hangfire", StringComparison.OrdinalIgnoreCase);

    private static bool IsPdfExport(PathString path) =>
        path.Value is { } value
        && value.Contains("/export/", StringComparison.OrdinalIgnoreCase)
        && value.EndsWith("/pdf", StringComparison.OrdinalIgnoreCase);

    // Exact path match, so /api/audit/parse does not count as an audit start
    private static bool IsPost(HttpContext context, string path) =>
        HttpMethods.IsPost(context.Request.Method)
        && string.Equals(context.Request.Path.Value?.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);

    private static bool HasCredentials(HttpContext context) =>
        !string.IsNullOrEmpty(context.Request.Headers.Authorization)
        || context.Request.Cookies.ContainsKey(HangfireDashboardAccess.CookieName);

    private async Task<bool> IsOverLimitAsync(string prefix, string ip, RateLimitTier tier)
    {
        var minuteCount = await _counters.IncrementAsync($"{prefix}:1m:{ip}", TimeSpan.FromMinutes(1));
        var fiveMinuteCount = await _counters.IncrementAsync($"{prefix}:5m:{ip}", TimeSpan.FromMinutes(5));

        return minuteCount > tier.PerMinute || fiveMinuteCount > tier.PerFiveMinutes;
    }

    private static async Task ReturnRateLimitError(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.Headers["Retry-After"] = "60";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Rate Limit Exceeded",
            message = message
        });
    }
}
