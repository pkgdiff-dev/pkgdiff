using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;
using PkgDiff.Tests.TestHost;
using static PkgDiff.Tests.Authorization.AuthorizationTestHost;

namespace PkgDiff.Tests.Authorization;

public class AdminAccountTakeoverTests
{
    private const string NewPassword = "N3w-Passw0rd!";

    private sealed class FixedTokenProvider : IUserTwoFactorTokenProvider<ApplicationUser>
    {
        public Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult("token");

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult(token == "token");

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult(false);
    }

    private static InMemoryIdentityStore CreateStore()
    {
        var store = new InMemoryIdentityStore();
        store.AddRole(AdminRole);
        store.AddRole(ViewerRole);
        return store;
    }

    private static AdminUserService CreateService(InMemoryIdentityStore store)
    {
        var userManager = store.CreateUserManager();
        userManager.RegisterTokenProvider(TokenOptions.DefaultProvider, new FixedTokenProvider());
        return new AdminUserService(userManager, store.CreateRoleManager());
    }

    private static async Task<HttpStatusCode> ResetPasswordOverHttpAsync(
        InMemoryIdentityStore store,
        ApplicationUser target,
        string roles,
        params string[] permissions)
    {
        await using var app = await StartAsync(services => services.AddSingleton<IAdminUserService>(CreateService(store)));
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{target.Id}/password")
        {
            Content = JsonContent.Create(new ResetUserPasswordRequest { NewPassword = NewPassword })
        };
        request.Headers.Add(TestAuthHandler.UserHeader, "test-user");
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        if (permissions.Length > 0)
        {
            request.Headers.Add(TestAuthHandler.PermissionsHeader, string.Join(",", permissions));
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task ResetUserPasswordAsync_NonAdminCallerResetsAdmin_ThrowsAndKeepsThePassword()
    {
        var store = CreateStore();
        var admin = store.AddUser("admin", AdminRole);
        admin.PasswordHash = "original-hash";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(store).ResetUserPasswordAsync(
            admin.Id,
            new ResetUserPasswordRequest { NewPassword = NewPassword },
            callerIsAdmin: false, callerPermissions: []));

        Assert.Equal("original-hash", admin.PasswordHash);
    }

    [Fact]
    public async Task ResetUserPasswordAsync_AdminCallerResetsAdmin_ChangesThePassword()
    {
        var store = CreateStore();
        var admin = store.AddUser("admin", AdminRole);
        admin.PasswordHash = "original-hash";

        await CreateService(store).ResetUserPasswordAsync(
            admin.Id,
            new ResetUserPasswordRequest { NewPassword = NewPassword },
            callerIsAdmin: true, callerPermissions: []);

        Assert.NotEqual("original-hash", admin.PasswordHash);
    }

    [Fact]
    public async Task ResetUserPasswordAsync_NonAdminCallerResetsViewer_ChangesThePassword()
    {
        var store = CreateStore();
        var viewer = store.AddUser("viewer", ViewerRole);
        viewer.PasswordHash = "original-hash";

        await CreateService(store).ResetUserPasswordAsync(
            viewer.Id,
            new ResetUserPasswordRequest { NewPassword = NewPassword },
            callerIsAdmin: false, callerPermissions: []);

        Assert.NotEqual("original-hash", viewer.PasswordHash);
    }

    [Fact]
    public async Task ResetUserPassword_NonAdminWithUsersEdit_ResettingAnAdmin_IsForbidden()
    {
        var store = CreateStore();
        var admin = store.AddUser("admin", AdminRole);
        admin.PasswordHash = "original-hash";

        var status = await ResetPasswordOverHttpAsync(store, admin, "UserManager", Permissions.Users.Edit);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("original-hash", admin.PasswordHash);
    }

    [Fact]
    public async Task ResetUserPassword_AdminResettingAnAdmin_Succeeds()
    {
        var store = CreateStore();
        var admin = store.AddUser("admin", AdminRole);
        admin.PasswordHash = "original-hash";

        var status = await ResetPasswordOverHttpAsync(store, admin, AdminRole, Permissions.Users.Edit);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEqual("original-hash", admin.PasswordHash);
    }

    [Fact]
    public async Task UpdateUserAsync_NonAdminCallerChangesAdminEmail_ThrowsAndChangesNothing()
    {
        var store = CreateStore();
        var admin = store.AddUser("admin", AdminRole);
        var originalEmail = admin.Email;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(store).UpdateUserAsync(
            admin.Id,
            new UpdateUserRequest { Email = "attacker@example.test", Roles = [AdminRole] },
            callerIsAdmin: false, callerPermissions: []));

        Assert.Equal(originalEmail, admin.Email);
        Assert.Equal([AdminRole], store.RolesOf(admin));
    }

    [Fact]
    public async Task UpdateUserAsync_AdminCallerChangesAdminEmail_Succeeds()
    {
        var store = CreateStore();
        var admin = store.AddUser("admin", AdminRole);

        await CreateService(store).UpdateUserAsync(
            admin.Id,
            new UpdateUserRequest { Email = "new-admin@example.test", Roles = [AdminRole] },
            callerIsAdmin: true, callerPermissions: []);

        Assert.Equal("new-admin@example.test", admin.Email);
    }

    [Fact]
    public async Task UpdateUserAsync_NonAdminCallerChangesViewerEmail_Succeeds()
    {
        var store = CreateStore();
        var viewer = store.AddUser("viewer", ViewerRole);

        await CreateService(store).UpdateUserAsync(
            viewer.Id,
            new UpdateUserRequest { Email = "viewer-new@example.test", Roles = [ViewerRole] },
            callerIsAdmin: false, callerPermissions: []);

        Assert.Equal("viewer-new@example.test", viewer.Email);
    }
}
