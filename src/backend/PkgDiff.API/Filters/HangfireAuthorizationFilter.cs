using Hangfire.Dashboard;
using PkgDiff.API.Services;

namespace PkgDiff.API.Filters;

// Re-checks against the database on every dashboard request that the cookie or bearer user is still an active Admin.
public class HangfireAuthorizationFilter : IDashboardAsyncAuthorizationFilter
{
    public Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var access = httpContext.RequestServices.GetRequiredService<HangfireDashboardAccess>();
        var checker = httpContext.RequestServices.GetRequiredService<IAdminRoleChecker>();
        return access.IsAuthorizedAsync(httpContext, checker);
    }
}
