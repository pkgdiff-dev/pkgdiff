using System.Net;
using Microsoft.AspNetCore.TestHost;
using PkgDiff.Shared.Constants;
using static PkgDiff.Tests.Authorization.AuthorizationTestHost;

namespace PkgDiff.Tests.Authorization;

public class AccessControlTests
{
    public static TheoryData<string, string> BlogAdminEndpoints => new()
    {
        { "GET", "/api/admin/blog" },
        { "POST", "/api/admin/blog/trigger" },
        { "PUT", "/api/admin/blog/1/toggle-publish" },
        { "PUT", "/api/admin/blog/1" },
        { "DELETE", "/api/admin/blog/1" }
    };

    private static async Task<HttpStatusCode> SendAsync(
        string method,
        string path,
        string? roles,
        params string[] permissions)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(new HttpMethod(method), path, roles, permissions);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    // 404 would mean the route never reached authorization, which must not count as a pass.
    private static void AssertAuthorized(HttpStatusCode status)
    {
        Assert.NotEqual(HttpStatusCode.NotFound, status);
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
    }

    [Theory]
    [MemberData(nameof(BlogAdminEndpoints))]
    public async Task BlogAdmin_Viewer_IsForbidden(string method, string path)
    {
        var status = await SendAsync(method, path, ViewerRole, ViewerPermissions);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Theory]
    [MemberData(nameof(BlogAdminEndpoints))]
    public async Task BlogAdmin_Anonymous_IsUnauthorized(string method, string path)
    {
        var status = await SendAsync(method, path, roles: null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Theory]
    [MemberData(nameof(BlogAdminEndpoints))]
    public async Task BlogAdmin_Admin_PassesAuthorization(string method, string path)
    {
        var status = await SendAsync(method, path, AdminRole);

        AssertAuthorized(status);
    }

    [Fact]
    public async Task BlogAdmin_BlogViewOnly_CanListButNotPublish()
    {
        var list = await SendAsync("GET", "/api/admin/blog", "", Permissions.Blog.View);
        var publish = await SendAsync("PUT", "/api/admin/blog/1/toggle-publish", "", Permissions.Blog.View);

        AssertAuthorized(list);
        Assert.Equal(HttpStatusCode.Forbidden, publish);
    }

    [Fact]
    public async Task BlogAdmin_BlogManage_CanPublish()
    {
        var status = await SendAsync("PUT", "/api/admin/blog/1/toggle-publish", "", Permissions.Blog.Manage);

        AssertAuthorized(status);
    }

    public static TheoryData<string, string> ManagementEndpoints => new()
    {
        { "GET", "/api/contributors/paged" },
        { "POST", "/api/contributors/backfill" },
        { "GET", "/api/admin/security/ip-statistics" },
        { "GET", "/api/admin/social-media" },
        { "POST", "/api/admin/social-media/share" },
        { "GET", "/api/admin/social-media/linkedin/auth-url" },
        { "POST", "/api/admin/social-media/linkedin/callback" }
    };

    [Theory]
    [MemberData(nameof(ManagementEndpoints))]
    public async Task Management_Viewer_IsForbidden(string method, string path)
    {
        var status = await SendAsync(method, path, ViewerRole, ViewerPermissions);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Theory]
    [MemberData(nameof(ManagementEndpoints))]
    public async Task Management_Admin_PassesAuthorization(string method, string path)
    {
        var status = await SendAsync(method, path, AdminRole);

        AssertAuthorized(status);
    }

    [Fact]
    public async Task Contributors_PublicLeaderboard_StaysAnonymous()
    {
        var status = await SendAsync("GET", "/api/contributors", roles: null);

        AssertAuthorized(status);
    }

    [Theory]
    [InlineData("/api/contributors/paged")]
    [InlineData("/api/admin/social-media")]
    public async Task Management_Anonymous_IsUnauthorized(string path)
    {
        var status = await SendAsync("GET", path, roles: null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task IpStatistics_ViewLogsOnly_IsForbidden()
    {
        var status = await SendAsync("GET", "/api/admin/security/ip-statistics", "", Permissions.Security.ViewLogs);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task IpStatistics_ManageIpsOnly_IsForbidden()
    {
        var status = await SendAsync("GET", "/api/admin/security/ip-statistics", "", Permissions.Security.ManageIps);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task IpStatistics_ViewLogsAndManageIps_PassesAuthorization()
    {
        var status = await SendAsync(
            "GET",
            "/api/admin/security/ip-statistics",
            "",
            Permissions.Security.ViewLogs,
            Permissions.Security.ManageIps);

        AssertAuthorized(status);
    }

    [Theory]
    [InlineData("GET", "/api/contributors/paged")]
    [InlineData("POST", "/api/contributors/backfill")]
    public async Task Contributors_ContributorsManage_PassesAuthorization(string method, string path)
    {
        var status = await SendAsync(method, path, "", Permissions.Contributors.Manage);

        AssertAuthorized(status);
    }

    [Fact]
    public async Task SocialMedia_ViewOnly_CanReadHistoryButNotShare()
    {
        var history = await SendAsync("GET", "/api/admin/social-media", "", Permissions.SocialMedia.View);
        var share = await SendAsync("POST", "/api/admin/social-media/share", "", Permissions.SocialMedia.View);

        AssertAuthorized(history);
        Assert.Equal(HttpStatusCode.Forbidden, share);
    }

    [Theory]
    [InlineData("POST", "/api/admin/social-media/share")]
    [InlineData("GET", "/api/admin/social-media/linkedin/auth-url")]
    [InlineData("POST", "/api/admin/social-media/linkedin/callback")]
    public async Task SocialMedia_Manage_PassesAuthorization(string method, string path)
    {
        var status = await SendAsync(method, path, "", Permissions.SocialMedia.Manage);

        AssertAuthorized(status);
    }

    [Fact]
    public async Task SocialMedia_ModeratorRole_IsForbidden()
    {
        var status = await SendAsync("GET", "/api/admin/social-media", "Moderator");

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }
}
