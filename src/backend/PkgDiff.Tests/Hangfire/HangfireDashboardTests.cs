using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PkgDiff.API.Controllers;
using PkgDiff.API.Extensions;
using PkgDiff.API.Middleware;
using PkgDiff.API.Services;
using PkgDiff.Shared.Constants;
using PkgDiff.Tests.TestHost;

namespace PkgDiff.Tests.Hangfire;

public class HangfireDashboardTests
{
    private const string AdminUser = "admin-user";
    private const string ViewerUser = "viewer-user";

    private sealed class FakeAdminRoleChecker : IAdminRoleChecker
    {
        private readonly HashSet<string> _admins;

        public FakeAdminRoleChecker(params string[] admins)
        {
            _admins = new HashSet<string>(admins, StringComparer.Ordinal);
        }

        public Task<bool> IsActiveAdminAsync(string userId) => Task.FromResult(_admins.Contains(userId));
    }

    private static HangfireDashboardAccess CreateAccess(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider(),
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    [Fact]
    public async Task RedeemTicket_ValidTicket_ReturnsUserIdOnce()
    {
        var access = CreateAccess();
        var ticket = access.CreateTicket("u1");

        Assert.Equal("u1", await access.RedeemTicketAsync(ticket));
        Assert.Null(await access.RedeemTicketAsync(ticket));
    }

    [Fact]
    public async Task RedeemTicket_TamperedTicket_ReturnsNull()
    {
        var access = CreateAccess();
        var ticket = access.CreateTicket("u1");
        var tampered = ticket[..^2] + (ticket[^2] == 'A' ? "B" : "A") + ticket[^1];

        Assert.Null(await access.RedeemTicketAsync(tampered));
        Assert.Null(await access.RedeemTicketAsync("garbage"));
        Assert.Null(await access.RedeemTicketAsync(string.Empty));
    }

    [Fact]
    public async Task RedeemTicket_ExpiredTicket_ReturnsNull()
    {
        var access = CreateAccess();
        var ticket = access.CreateTicket("u1", DateTimeOffset.UtcNow.AddSeconds(-5));

        Assert.Null(await access.RedeemTicketAsync(ticket));
    }

    [Fact]
    public async Task RedeemTicket_CookieValueIsNotAcceptedAsTicket()
    {
        var access = CreateAccess();

        Assert.Null(await access.RedeemTicketAsync(access.CreateCookieValue("u1")));
        Assert.Null(access.ReadCookieValue(access.CreateTicket("u1")));
    }

    [Fact]
    public void CookieValue_RoundTripsAndRejectsGarbage()
    {
        var access = CreateAccess();

        Assert.Equal("u1", access.ReadCookieValue(access.CreateCookieValue("u1")));
        Assert.Null(access.ReadCookieValue("garbage"));
        Assert.Null(access.ReadCookieValue(null));
    }

    private static DefaultHttpContext ContextWithCookie(string? cookieValue, ClaimsPrincipal? user = null)
    {
        var context = new DefaultHttpContext();
        if (cookieValue is not null)
            context.Request.Headers.Cookie = $"{HangfireDashboardAccess.CookieName}={cookieValue}";
        if (user is not null)
            context.User = user;
        return context;
    }

    [Fact]
    public async Task IsAuthorized_CookieForActiveAdmin_True()
    {
        var access = CreateAccess();
        var context = ContextWithCookie(access.CreateCookieValue(AdminUser));

        Assert.True(await access.IsAuthorizedAsync(context, new FakeAdminRoleChecker(AdminUser)));
    }

    [Fact]
    public async Task IsAuthorized_CookieForUserNoLongerAdmin_False()
    {
        var access = CreateAccess();
        var context = ContextWithCookie(access.CreateCookieValue(AdminUser));

        Assert.False(await access.IsAuthorizedAsync(context, new FakeAdminRoleChecker()));
    }

    [Fact]
    public async Task IsAuthorized_NoCookieAnonymous_False()
    {
        var access = CreateAccess();

        Assert.False(await access.IsAuthorizedAsync(ContextWithCookie(null), new FakeAdminRoleChecker(AdminUser)));
    }

    [Fact]
    public async Task IsAuthorized_NoCookieAdminPrincipalStillActiveAdmin_True()
    {
        var access = CreateAccess();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, AdminUser), new Claim(ClaimTypes.Role, "Admin")], "Bearer"));

        Assert.True(await access.IsAuthorizedAsync(ContextWithCookie(null, principal), new FakeAdminRoleChecker(AdminUser)));
    }

    [Fact]
    public async Task IsAuthorized_SubClaimOnlyAdminPrincipalStillActiveAdmin_True()
    {
        var access = CreateAccess();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", AdminUser), new Claim(ClaimTypes.Role, "Admin")], "Bearer"));

        Assert.True(await access.IsAuthorizedAsync(ContextWithCookie(null, principal), new FakeAdminRoleChecker(AdminUser)));
    }

    [Fact]
    public async Task IsAuthorized_NoCookieAdminPrincipalNoLongerAdmin_False()
    {
        var access = CreateAccess();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, AdminUser), new Claim(ClaimTypes.Role, "Admin")], "Bearer"));

        Assert.False(await access.IsAuthorizedAsync(ContextWithCookie(null, principal), new FakeAdminRoleChecker()));
    }

    [Fact]
    public async Task IsAuthorized_AdminRoleClaimWithoutUserId_False()
    {
        var access = CreateAccess();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Bearer"));

        Assert.False(await access.IsAuthorizedAsync(ContextWithCookie(null, principal), new FakeAdminRoleChecker(AdminUser)));
    }

    private static async Task<WebApplication> StartTicketHostAsync(HangfireDashboardAccess access)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(access);
        builder.Services.AddScoped<IAdminRoleChecker>(_ => new FakeAdminRoleChecker(AdminUser));

        var app = builder.Build();
        app.UseMiddleware<HangfireTicketMiddleware>();
        app.Run(ctx => ctx.Response.WriteAsync("dashboard"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Fact]
    public async Task TicketMiddleware_ValidAdminTicket_SetsScopedCookieAndRedirects()
    {
        var access = CreateAccess();
        await using var app = await StartTicketHostAsync(access);
        using var client = app.GetTestClient();
        var ticket = Uri.EscapeDataString(access.CreateTicket(AdminUser));

        using var response = await client.GetAsync($"/hangfire?ticket={ticket}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/hangfire", response.Headers.Location?.OriginalString);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.StartsWith(HangfireDashboardAccess.CookieName + "=", setCookie);
        Assert.Contains("httponly", setCookie);
        Assert.Contains("samesite=strict", setCookie);
        Assert.Contains("path=/hangfire", setCookie);
        Assert.Contains("secure", setCookie);
        Assert.Contains("max-age=1800", setCookie);

        using var replay = await client.GetAsync($"/hangfire?ticket={ticket}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.False(replay.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TicketMiddleware_GarbageTicket_Unauthorized()
    {
        var access = CreateAccess();
        await using var app = await StartTicketHostAsync(access);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/hangfire?ticket=garbage", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TicketMiddleware_TicketForNonAdmin_Forbidden()
    {
        var access = CreateAccess();
        await using var app = await StartTicketHostAsync(access);
        using var client = app.GetTestClient();
        var ticket = Uri.EscapeDataString(access.CreateTicket(ViewerUser));

        using var response = await client.GetAsync($"/hangfire?ticket={ticket}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TicketMiddleware_NoTicket_PassesThrough()
    {
        var access = CreateAccess();
        await using var app = await StartTicketHostAsync(access);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/hangfire", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dashboard", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<WebApplication> StartEndpointHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(HangfireDashboardController).Assembly);
        TestAuthHandler.Register(builder.Services);
        builder.Services.AddPkgDiffAuthorization();
        builder.Services.AddSingleton(CreateAccess());
        builder.Services.AddScoped<IAdminRoleChecker>(_ => new FakeAdminRoleChecker(AdminUser));

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static HttpRequestMessage TicketRequest(string? user, string? roles, params string[] permissions)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/jobs/dashboard-ticket")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
            if (roles is not null)
                request.Headers.Add(TestAuthHandler.RolesHeader, roles);
            if (permissions.Length > 0)
                request.Headers.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        }
        return request;
    }

    [Fact]
    public async Task TicketEndpoint_Anonymous_Unauthorized()
    {
        await using var app = await StartEndpointHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(TicketRequest(null, null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TicketEndpoint_ViewerWithJobsView_Forbidden()
    {
        await using var app = await StartEndpointHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(
            TicketRequest(ViewerUser, "Viewer", Permissions.Jobs.View), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TicketEndpoint_JobsManageButNotAdmin_Forbidden()
    {
        await using var app = await StartEndpointHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(
            TicketRequest(ViewerUser, "Operator", Permissions.Jobs.Manage), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TicketEndpoint_Admin_ReturnsDashboardUrl()
    {
        await using var app = await StartEndpointHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(TicketRequest(AdminUser, "Admin"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var url = json.RootElement.GetProperty("url").GetString();
        Assert.NotNull(url);
        Assert.StartsWith("/hangfire?ticket=", url);

        var access = app.Services.GetRequiredService<HangfireDashboardAccess>();
        var ticket = Uri.UnescapeDataString(url["/hangfire?ticket=".Length..]);
        Assert.Equal(AdminUser, await access.RedeemTicketAsync(ticket));
    }
}
