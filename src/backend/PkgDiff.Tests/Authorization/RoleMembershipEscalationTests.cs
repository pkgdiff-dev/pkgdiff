using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Options;
using PkgDiff.Tests.TestDoubles;
using PkgDiff.Tests.TestHost;
using static PkgDiff.Tests.Authorization.AuthorizationTestHost;

namespace PkgDiff.Tests.Authorization;

public class RoleMembershipEscalationTests
{
    private const string SupportRole = "Support";
    private const string OperatorRole = "Operator";
    private const string NewPassword = "N3w-Passw0rd!";

    private static readonly string[] SupportPermissions =
        [Permissions.Users.View, Permissions.Users.Create, Permissions.Users.Edit];

    private static readonly string[] OperatorPermissions =
        [Permissions.Settings.Edit, Permissions.Jobs.Manage, Permissions.Roles.Edit];

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
        store.AddRole(SupportRole);
        store.AddRole(OperatorRole);
        Grant(store, SupportRole, SupportPermissions);
        Grant(store, OperatorRole, OperatorPermissions);
        Grant(store, ViewerRole, Permissions.Users.View);
        return store;
    }

    private static void Grant(InMemoryIdentityStore store, string roleName, params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            store.AddClaimAsync(store.RoleNamed(roleName), new Claim("Permission", permission)).GetAwaiter().GetResult();
        }
    }

    private static AdminUserService CreateService(InMemoryIdentityStore store)
    {
        var userManager = store.CreateUserManager();
        userManager.RegisterTokenProvider(TokenOptions.DefaultProvider, new FixedTokenProvider());
        return new AdminUserService(userManager, store.CreateRoleManager());
    }

    private static AuthService CreateAuthService(InMemoryIdentityStore store)
    {
        var userManager = store.CreateUserManager();
        var identityOptions = Options.Create(new IdentityOptions());
        var signInManager = new SignInManager<ApplicationUser>(
            userManager,
            new HttpContextAccessor(),
            new UserClaimsPrincipalFactory<ApplicationUser>(userManager, identityOptions),
            identityOptions,
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions())),
            new DefaultUserConfirmation<ApplicationUser>());
        return new AuthService(userManager, signInManager, store.CreateRoleManager(), new JwtSettings());
    }

    [Fact]
    public void EvaluatePermissionSubset_NonAdminLackingAPermission_IsForbidden()
    {
        var (allowed, status, error) = RoleAssignmentRules.EvaluatePermissionSubset(
            OperatorPermissions, callerIsAdmin: false, SupportPermissions, "nope");

        Assert.False(allowed);
        Assert.Equal(403, status);
        Assert.Equal("nope", error);
    }

    [Fact]
    public void EvaluatePermissionSubset_SubsetOrAdmin_IsAllowed()
    {
        Assert.True(RoleAssignmentRules.EvaluatePermissionSubset([Permissions.Users.View], false, SupportPermissions, "x").Allowed);
        Assert.True(RoleAssignmentRules.EvaluatePermissionSubset([], false, [], "x").Allowed);
        Assert.True(RoleAssignmentRules.EvaluatePermissionSubset(OperatorPermissions, true, [], "x").Allowed);
    }

    [Fact]
    public async Task NonAdmin_AssigningARicherRoleToSelf_IsForbiddenAndChangesNothing()
    {
        var store = CreateStore();
        var self = store.AddUser("support", SupportRole);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(store).UpdateUserAsync(
            self.Id,
            new UpdateUserRequest { Email = self.Email!, Roles = [SupportRole, OperatorRole] },
            callerIsAdmin: false,
            callerPermissions: SupportPermissions));

        Assert.Equal([SupportRole], store.RolesOf(self));
    }

    [Fact]
    public async Task NonAdmin_AssigningARoleWithinTheirPermissions_Succeeds()
    {
        var store = CreateStore();
        var target = store.AddUser("target");

        await CreateService(store).UpdateUserAsync(
            target.Id,
            new UpdateUserRequest { Email = target.Email!, Roles = [ViewerRole] },
            callerIsAdmin: false,
            callerPermissions: SupportPermissions);

        Assert.Equal([ViewerRole], store.RolesOf(target));
    }

    [Fact]
    public async Task Admin_AssigningARicherRole_Succeeds()
    {
        var store = CreateStore();
        var target = store.AddUser("target", ViewerRole);

        await CreateService(store).UpdateUserAsync(
            target.Id,
            new UpdateUserRequest { Email = target.Email!, Roles = [OperatorRole] },
            callerIsAdmin: true,
            callerPermissions: []);

        Assert.Equal([OperatorRole], store.RolesOf(target));
    }

    [Fact]
    public async Task NonAdmin_CreatingAUserInARicherRole_IsForbiddenAndCreatesNoUser()
    {
        var store = CreateStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateUserAsync(
            new CreateUserRequest { Username = "sock", Email = "sock@example.test", Password = NewPassword, Roles = [OperatorRole] },
            callerIsAdmin: false,
            callerPermissions: SupportPermissions));

        Assert.Null(await store.CreateUserManager().FindByNameAsync("sock"));
    }

    [Fact]
    public async Task NonAdmin_RegisteringAUserInARicherRole_IsForbiddenAndCreatesNoUser()
    {
        var store = CreateStore();

        var (status, errors) = await CreateAuthService(store).RegisterAsync(
            "sock", "sock@example.test", NewPassword, OperatorRole, callerIsAdmin: false, SupportPermissions);

        Assert.Equal(403, status);
        Assert.NotEmpty(errors);
        Assert.Null(await store.CreateUserManager().FindByNameAsync("sock"));
    }

    [Fact]
    public async Task NonAdmin_RegisteringAUserInARoleWithinTheirPermissions_Succeeds()
    {
        var store = CreateStore();

        var (status, _) = await CreateAuthService(store).RegisterAsync(
            "viewer", "viewer@example.test", NewPassword, ViewerRole, callerIsAdmin: false, SupportPermissions);

        Assert.Equal(200, status);
    }

    [Fact]
    public async Task NonAdmin_ResettingARicherNonAdminsPassword_IsForbiddenAndKeepsThePassword()
    {
        var store = CreateStore();
        var richer = store.AddUser("operator", OperatorRole);
        richer.PasswordHash = "original-hash";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(store).ResetUserPasswordAsync(
            richer.Id,
            new ResetUserPasswordRequest { NewPassword = NewPassword },
            callerIsAdmin: false,
            callerPermissions: SupportPermissions));

        Assert.Equal("original-hash", richer.PasswordHash);
    }

    [Fact]
    public async Task NonAdmin_ResettingThePasswordOfAUserWithinTheirPermissions_Succeeds()
    {
        var store = CreateStore();
        var viewer = store.AddUser("viewer", ViewerRole);
        viewer.PasswordHash = "original-hash";

        await CreateService(store).ResetUserPasswordAsync(
            viewer.Id,
            new ResetUserPasswordRequest { NewPassword = NewPassword },
            callerIsAdmin: false,
            callerPermissions: SupportPermissions);

        Assert.NotEqual("original-hash", viewer.PasswordHash);
    }

    [Fact]
    public async Task NonAdmin_EditingARicherNonAdminsEmail_IsForbiddenAndChangesNothing()
    {
        var store = CreateStore();
        var richer = store.AddUser("operator", OperatorRole);
        var originalEmail = richer.Email;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(store).UpdateUserAsync(
            richer.Id,
            new UpdateUserRequest { Email = "attacker@example.test", Roles = [OperatorRole] },
            callerIsAdmin: false,
            callerPermissions: SupportPermissions));

        Assert.Equal(originalEmail, richer.Email);
    }

    [Fact]
    public async Task ResetUserPassword_OverHttp_NonAdminResettingARicherNonAdmin_IsForbidden()
    {
        var store = CreateStore();
        var richer = store.AddUser("operator", OperatorRole);
        richer.PasswordHash = "original-hash";

        await using var app = await StartAsync(services => services.AddSingleton<IAdminUserService>(CreateService(store)));
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{richer.Id}/password")
        {
            Content = JsonContent.Create(new ResetUserPasswordRequest { NewPassword = NewPassword })
        };
        request.Headers.Add(TestAuthHandler.UserHeader, "test-user");
        request.Headers.Add(TestAuthHandler.RolesHeader, SupportRole);
        request.Headers.Add(TestAuthHandler.PermissionsHeader, string.Join(",", SupportPermissions));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("original-hash", richer.PasswordHash);
    }

    [Fact]
    public async Task UpdateUser_OverHttp_NonAdminAddingARicherRoleToSelf_IsForbidden()
    {
        var store = CreateStore();
        var self = store.AddUser("support", SupportRole);

        await using var app = await StartAsync(services => services.AddSingleton<IAdminUserService>(CreateService(store)));
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{self.Id}")
        {
            Content = JsonContent.Create(new UpdateUserRequest { Email = self.Email!, Roles = [SupportRole, OperatorRole] })
        };
        request.Headers.Add(TestAuthHandler.UserHeader, self.Id);
        request.Headers.Add(TestAuthHandler.RolesHeader, SupportRole);
        request.Headers.Add(TestAuthHandler.PermissionsHeader, string.Join(",", SupportPermissions));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal([SupportRole], store.RolesOf(self));
    }
}
