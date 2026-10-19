using Hangfire.Dashboard;
using PkgDiff.API.Services;

namespace PkgDiff.API.Filters;

// Re-checks the database on every request that the user is still an active Admin
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
