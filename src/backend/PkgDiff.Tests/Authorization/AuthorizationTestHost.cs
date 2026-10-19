using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.API.Extensions;
using PkgDiff.Shared.Constants;
using PkgDiff.Tests.TestHost;

namespace PkgDiff.Tests.Authorization;

public static class AuthorizationTestHost
{
    public const string ViewerRole = "Viewer";
    public const string AdminRole = "Admin";

    // Mirrors the Viewer permissions seeded by DatabaseSeeder.
    public static readonly string[] ViewerPermissions =
    [
        Permissions.Users.View,
        Permissions.Roles.View,
        Permissions.Packages.View,
        Permissions.Registries.View,
        Permissions.Settings.View,
        Permissions.Security.ViewLogs,
        Permissions.Health.View,
        Permissions.Jobs.View,
        Permissions.VersionExcludePatterns.View,
        Permissions.ScrapeLogs.View
    ];

    // Controllers are not registered: a 500 from their activation means authorization passed
    public static async Task<WebApplication> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PkgDiff.API.Controllers.AdminBlogController).Assembly);
        TestAuthHandler.Register(builder.Services);
        builder.Services.AddPkgDiffAuthorization();
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            }
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    public static HttpRequestMessage Request(HttpMethod method, string path, string? roles, params string[] permissions)
    {
        var request = new HttpRequestMessage(method, path);

        if (roles is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, "test-user");
            request.Headers.Add(TestAuthHandler.RolesHeader, roles);
            if (permissions.Length > 0)
            {
                request.Headers.Add(TestAuthHandler.PermissionsHeader, string.Join(",", permissions));
            }
        }

        if (method == HttpMethod.Put || method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }
}
