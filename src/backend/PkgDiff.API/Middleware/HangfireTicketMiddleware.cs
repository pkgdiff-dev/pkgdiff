using PkgDiff.API.Services;

namespace PkgDiff.API.Middleware;

// Exchanges a one-time dashboard ticket for the /hangfire cookie. This cannot live in the Hangfire
// authorization filter because Hangfire overwrites the response status when a filter denies access.
public class HangfireTicketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HangfireDashboardAccess _access;
    private readonly IHostEnvironment _environment;

    public HangfireTicketMiddleware(
        RequestDelegate next,
        HangfireDashboardAccess access,
        IHostEnvironment environment)
    {
        _next = next;
        _access = access;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, IAdminRoleChecker checker)
    {
        if (!context.Request.Path.StartsWithSegments("/hangfire")
            || !context.Request.Query.TryGetValue("ticket", out var ticketValues)
            || string.IsNullOrWhiteSpace(ticketValues.ToString()))
        {
            await _next(context);
            return;
        }

        var userId = await _access.RedeemTicketAsync(ticketValues.ToString());
        if (userId is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!await checker.IsActiveAdminAsync(userId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Cookies.Append(
            HangfireDashboardAccess.CookieName,
            _access.CreateCookieValue(userId),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !_environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/hangfire",
                MaxAge = HangfireDashboardAccess.CookieLifetime,
                IsEssential = true
            });

        context.Response.Redirect(context.Request.PathBase + "/hangfire");
    }
}
