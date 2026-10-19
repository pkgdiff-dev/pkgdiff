using System.Security.Cryptography;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Authorization;

public class AdminRoleRemovalTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    public void EvaluateRemoval_AdminRoleByNonAdminCaller_IsForbidden(string role)
    {
        var (allowed, status, error) = RoleAssignmentRules.EvaluateRemoval([role], callerIsAdmin: false, adminCount: 3);

        Assert.False(allowed);
        Assert.Equal(403, status);
        Assert.NotNull(error);
    }

    [Fact]
    public void EvaluateRemoval_LastAdmin_IsRejected()
    {
        var (allowed, status, error) = RoleAssignmentRules.EvaluateRemoval(["Admin"], callerIsAdmin: true, adminCount: 1);

        Assert.False(allowed);
        Assert.Equal(400, status);
        Assert.NotNull(error);
    }

    [Fact]
    public void EvaluateRemoval_AdminByAdminWithAnotherAdminLeft_IsAllowed()
    {
        var (allowed, status, _) = RoleAssignmentRules.EvaluateRemoval(["Admin"], callerIsAdmin: true, adminCount: 2);

        Assert.True(allowed);
        Assert.Equal(200, status);
    }

    [Fact]
    public void EvaluateRemoval_NonAdminRoleByNonAdminCaller_IsAllowed()
    {
        var (allowed, _, _) = RoleAssignmentRules.EvaluateRemoval(["Viewer"], callerIsAdmin: false, adminCount: 1);

        Assert.True(allowed);
    }

    private static (InMemoryIdentityStore Store, AdminUserService Service) CreateService()
    {
        var store = new InMemoryIdentityStore();
        store.AddRole("Admin");
        store.AddRole("Viewer");
        var service = new AdminUserService(store.CreateUserManager(), store.CreateRoleManager());
        return (store, service);
    }

    [Fact]
    public async Task UpdateUser_NonAdminCallerRemovesAdminRole_IsForbiddenAndChangesNothing()
    {
        var (store, service) = CreateService();
        store.AddUser("first-admin", "Admin");
        var target = store.AddUser("second-admin", "Admin");
        var originalEmail = target.Email;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateUserAsync(
            target.Id,
            new UpdateUserRequest { Email = "changed@example.test", Roles = ["Viewer"] },
            callerIsAdmin: false, callerPermissions: []));

        Assert.Equal(["Admin"], store.RolesOf(target));
        Assert.Equal(originalEmail, target.Email);
    }

    [Fact]
    public async Task UpdateUser_AdminCallerRemovesLastAdmin_IsRejectedAndChangesNothing()
    {
        var (store, service) = CreateService();
        var onlyAdmin = store.AddUser("only-admin", "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateUserAsync(
            onlyAdmin.Id,
            new UpdateUserRequest { Email = onlyAdmin.Email!, Roles = ["Viewer"] },
            callerIsAdmin: true, callerPermissions: []));

        Assert.Equal(["Admin"], store.RolesOf(onlyAdmin));
    }

    [Fact]
    public async Task UpdateUser_AdminCallerRemovesAdminWithAnotherAdminLeft_Succeeds()
    {
        var (store, service) = CreateService();
        store.AddUser("first-admin", "Admin");
        var target = store.AddUser("second-admin", "Admin");

        await service.UpdateUserAsync(
            target.Id,
            new UpdateUserRequest { Email = target.Email!, Roles = ["Viewer"] },
            callerIsAdmin: true, callerPermissions: []);

        Assert.Equal(["Viewer"], store.RolesOf(target));
    }

    [Fact]
    public async Task UpdateUser_NonAdminCallerChangesViewerEmail_Succeeds()
    {
        var (store, service) = CreateService();
        store.AddUser("only-admin", "Admin");
        var viewer = store.AddUser("viewer", "Viewer");

        await service.UpdateUserAsync(
            viewer.Id,
            new UpdateUserRequest { Email = "new@example.test", Roles = ["Viewer"] },
            callerIsAdmin: false, callerPermissions: []);

        Assert.Equal("new@example.test", viewer.Email);
        Assert.Equal(["Viewer"], store.RolesOf(viewer));
    }
}
