using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Constants;
using PkgDiff.Tests.TestDoubles;
using static PkgDiff.Tests.Authorization.AuthorizationTestHost;

namespace PkgDiff.Tests.Authorization;

public class AdminUserDeletionTests
{
    private sealed record ErrorBody(string? Error);

    private static AdminUserService CreateService(InMemoryIdentityStore store) =>
        new(store.CreateUserManager(), store.CreateRoleManager());

    private static InMemoryIdentityStore CreateStore()
    {
        var store = new InMemoryIdentityStore();
        store.AddRole(AdminRole);
        store.AddRole(ViewerRole);
        return store;
    }

    private static async Task<bool> ExistsAsync(InMemoryIdentityStore store, ApplicationUser user) =>
        await store.CreateUserManager().FindByIdAsync(user.Id) is not null;

    private static async Task<(HttpStatusCode Status, string? Error)> DeleteAsync(
        InMemoryIdentityStore store,
        ApplicationUser target,
        string roles,
        params string[] permissions)
    {
        await using var app = await StartAsync(services => services.AddSingleton<IAdminUserService>(CreateService(store)));
        using var client = app.GetTestClient();
        using var request = Request(HttpMethod.Delete, $"/api/admin/users/{target.Id}", roles, permissions);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        string? error = null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest
            && response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
            error = body?.Error;
        }

        return (response.StatusCode, error);
    }

    [Fact]
    public void EvaluateDeletion_AdminTargetByNonAdminCaller_IsForbidden()
    {
        var (allowed, status, error) = RoleAssignmentRules.EvaluateDeletion(targetIsAdmin: true, callerIsAdmin: false, adminCount: 3);

        Assert.False(allowed);
        Assert.Equal(403, status);
        Assert.NotNull(error);
    }

    [Fact]
    public void EvaluateDeletion_LastAdmin_IsRejected()
    {
        var (allowed, status, error) = RoleAssignmentRules.EvaluateDeletion(targetIsAdmin: true, callerIsAdmin: true, adminCount: 1);

        Assert.False(allowed);
        Assert.Equal(400, status);
        Assert.NotNull(error);
    }

    [Fact]
    public void EvaluateDeletion_AdminTargetByAdminWithAnotherAdminLeft_IsAllowed()
    {
        var (allowed, status, _) = RoleAssignmentRules.EvaluateDeletion(targetIsAdmin: true, callerIsAdmin: true, adminCount: 2);

        Assert.True(allowed);
        Assert.Equal(200, status);
    }

    [Fact]
    public void EvaluateDeletion_NonAdminTargetByNonAdminCaller_IsAllowed()
    {
        var (allowed, _, _) = RoleAssignmentRules.EvaluateDeletion(targetIsAdmin: false, callerIsAdmin: false, adminCount: 1);

        Assert.True(allowed);
    }

    [Fact]
    public async Task DeleteUserAsync_NonAdminCallerDeletesAdmin_ThrowsUnauthorizedAndKeepsTheUser()
    {
        var store = CreateStore();
        store.AddUser("first-admin", AdminRole);
        var target = store.AddUser("second-admin", AdminRole);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(store).DeleteUserAsync(target.Id, callerIsAdmin: false));

        Assert.True(await ExistsAsync(store, target));
    }

    [Fact]
    public async Task DeleteUserAsync_LastAdminDeletingThemselves_IsRejectedAndKeepsTheUser()
    {
        var store = CreateStore();
        var onlyAdmin = store.AddUser("only-admin", AdminRole);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(store).DeleteUserAsync(onlyAdmin.Id, callerIsAdmin: true));

        Assert.True(await ExistsAsync(store, onlyAdmin));
        Assert.Equal([AdminRole], store.RolesOf(onlyAdmin));
    }

    [Fact]
    public async Task DeleteUser_NonAdminWithUsersDelete_DeletingAnAdmin_IsForbiddenAndKeepsTheUser()
    {
        var store = CreateStore();
        store.AddUser("first-admin", AdminRole);
        var target = store.AddUser("second-admin", AdminRole);

        var (status, error) = await DeleteAsync(store, target, "UserManager", Permissions.Users.Delete);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.True(await ExistsAsync(store, target));
    }

    [Fact]
    public async Task DeleteUser_NonAdminWithUsersDelete_DeletingTheLastAdmin_IsForbiddenAndKeepsTheUser()
    {
        var store = CreateStore();
        var onlyAdmin = store.AddUser("only-admin", AdminRole);

        var (status, _) = await DeleteAsync(store, onlyAdmin, "UserManager", Permissions.Users.Delete);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.True(await ExistsAsync(store, onlyAdmin));
    }

    [Fact]
    public async Task DeleteUser_AdminDeletingTheLastAdmin_IsRejectedAndKeepsTheUser()
    {
        var store = CreateStore();
        var onlyAdmin = store.AddUser("only-admin", AdminRole);

        var (status, error) = await DeleteAsync(store, onlyAdmin, AdminRole);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.True(await ExistsAsync(store, onlyAdmin));
    }

    [Fact]
    public async Task DeleteUser_AdminDeletingAnAdminWithAnotherAdminLeft_Succeeds()
    {
        var store = CreateStore();
        store.AddUser("first-admin", AdminRole);
        var target = store.AddUser("second-admin", AdminRole);

        var (status, _) = await DeleteAsync(store, target, AdminRole);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.False(await ExistsAsync(store, target));
    }

    [Fact]
    public async Task DeleteUser_NonAdminWithUsersDelete_DeletingAViewer_Succeeds()
    {
        var store = CreateStore();
        store.AddUser("only-admin", AdminRole);
        var viewer = store.AddUser("viewer", ViewerRole);

        var (status, _) = await DeleteAsync(store, viewer, "UserManager", Permissions.Users.Delete);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.False(await ExistsAsync(store, viewer));
    }
}
