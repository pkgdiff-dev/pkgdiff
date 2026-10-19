using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;
using PkgDiff.Tests.TestHost;
using static PkgDiff.Tests.Authorization.AuthorizationTestHost;

namespace PkgDiff.Tests.Authorization;

// WR-01: Roles.Edit must not let a non-Admin grant their own role permissions they do not hold.
public class RolePermissionEscalationTests
{
    private const string ManagerRole = "RoleManager";

    private static readonly string[] ManagerPermissions = [Permissions.Roles.View, Permissions.Roles.Edit, Permissions.Users.View];

    private static InMemoryIdentityStore CreateStore()
    {
        var store = new InMemoryIdentityStore();
        store.AddRole(AdminRole);
        store.AddRole(ViewerRole);
        store.AddRole(ManagerRole);
        var manager = store.RoleNamed(ManagerRole);
        foreach (var permission in ManagerPermissions)
        {
            store.AddClaimAsync(manager, new Claim("Permission", permission)).GetAwaiter().GetResult();
        }

        return store;
    }

    private static AdminRoleService CreateService(InMemoryIdentityStore store) =>
        new(store.CreateUserManager(), store.CreateRoleManager());

    private static UpdateRolePermissionsRequest RequestFor(InMemoryIdentityStore store, string roleName, params string[] permissions) =>
        new() { RoleId = store.RoleNamed(roleName).Id, Permissions = permissions.ToList() };

    [Fact]
    public async Task NonAdmin_GrantingTheirRoleAPermissionTheyLack_IsForbiddenAndChangesNothing()
    {
        var store = CreateStore();
        var request = RequestFor(store, ManagerRole, [.. ManagerPermissions, Permissions.Settings.Edit]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(store).UpdateRolePermissionsAsync(request, callerIsAdmin: false, ManagerPermissions));

        Assert.Equal(ManagerPermissions.Order(), store.PermissionsOf(ManagerRole).Order());
    }

    [Fact]
    public async Task NonAdmin_RevokingAPermissionTheyLack_IsForbidden()
    {
        var store = CreateStore();
        await store.AddClaimAsync(store.RoleNamed(ViewerRole), new Claim("Permission", Permissions.Settings.Edit), TestContext.Current.CancellationToken);
        var request = RequestFor(store, ViewerRole);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(store).UpdateRolePermissionsAsync(request, callerIsAdmin: false, ManagerPermissions));

        Assert.Equal([Permissions.Settings.Edit], store.PermissionsOf(ViewerRole));
    }

    [Fact]
    public async Task NonAdmin_GrantingPermissionsTheyHold_Succeeds()
    {
        var store = CreateStore();
        var request = RequestFor(store, ViewerRole, Permissions.Roles.View, Permissions.Users.View);

        await CreateService(store).UpdateRolePermissionsAsync(request, callerIsAdmin: false, ManagerPermissions);

        Assert.Equal(new[] { Permissions.Roles.View, Permissions.Users.View }.Order(), store.PermissionsOf(ViewerRole).Order());
    }

    [Fact]
    public async Task Admin_GrantingAnyKnownPermission_Succeeds()
    {
        var store = CreateStore();
        var request = RequestFor(store, ManagerRole, Permissions.Settings.Edit, Permissions.Jobs.Manage);

        await CreateService(store).UpdateRolePermissionsAsync(request, callerIsAdmin: true, []);

        Assert.Equal(new[] { Permissions.Settings.Edit, Permissions.Jobs.Manage }.Order(), store.PermissionsOf(ManagerRole).Order());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownPermission_IsRejectedAndChangesNothing(bool callerIsAdmin)
    {
        var store = CreateStore();
        var request = RequestFor(store, ManagerRole, [.. ManagerPermissions, "Permissions.Anything.Goes"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(store).UpdateRolePermissionsAsync(request, callerIsAdmin, ManagerPermissions));

        Assert.Equal(ManagerPermissions.Order(), store.PermissionsOf(ManagerRole).Order());
    }

    [Fact]
    public async Task AdminRole_CannotBeEdited()
    {
        var store = CreateStore();
        var request = RequestFor(store, AdminRole, Permissions.Users.View);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(store).UpdateRolePermissionsAsync(request, callerIsAdmin: true, []));
    }

    private static async Task<HttpStatusCode> PutPermissionsAsync(
        InMemoryIdentityStore store,
        UpdateRolePermissionsRequest body,
        string roles,
        params string[] permissions)
    {
        await using var app = await StartAsync(services => services.AddSingleton<IAdminRoleService>(CreateService(store)));
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/roles/{body.RoleId}/permissions")
        {
            Content = JsonContent.Create(body)
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
    public async Task Http_NonAdminWithRolesEdit_EscalatingTheirRole_IsForbidden()
    {
        var store = CreateStore();
        var body = RequestFor(store, ManagerRole, [.. ManagerPermissions, Permissions.Users.Edit]);

        var status = await PutPermissionsAsync(store, body, ManagerRole, ManagerPermissions);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain(Permissions.Users.Edit, store.PermissionsOf(ManagerRole));
    }

    [Fact]
    public async Task Http_UnknownPermission_IsBadRequest()
    {
        var store = CreateStore();
        var body = RequestFor(store, ViewerRole, "Permissions.Made.Up");

        var status = await PutPermissionsAsync(store, body, AdminRole);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Http_AdminGrantingPermissions_Succeeds()
    {
        var store = CreateStore();
        var body = RequestFor(store, ViewerRole, Permissions.Settings.Edit);

        var status = await PutPermissionsAsync(store, body, AdminRole);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal([Permissions.Settings.Edit], store.PermissionsOf(ViewerRole));
    }
}
