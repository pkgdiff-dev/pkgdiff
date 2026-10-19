using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PkgDiff.API.Controllers;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Tests.Identity;

public class RoleAssignmentRulesTests
{
    private static readonly string[] ExistingRoles = ["Admin", "Viewer"];

    [Fact]
    public void Defaults_AreViewerAndAdmin()
    {
        Assert.Equal("Viewer", RoleAssignmentRules.DefaultRole);
        Assert.Equal("Admin", RoleAssignmentRules.AdminRole);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_ExistingNonAdminRole_IsAllowed(bool callerIsAdmin)
    {
        var (allowed, status, error) = RoleAssignmentRules.Evaluate(["Viewer"], callerIsAdmin, ExistingRoles);

        Assert.True(allowed);
        Assert.Equal(200, status);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    public void Evaluate_AdminRoleByNonAdminCaller_IsForbidden(string role)
    {
        var (allowed, status, error) = RoleAssignmentRules.Evaluate([role], false, ExistingRoles);

        Assert.False(allowed);
        Assert.Equal(403, status);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Evaluate_AdminRoleByAdminCaller_IsAllowed()
    {
        var (allowed, status, error) = RoleAssignmentRules.Evaluate(["Admin"], true, ExistingRoles);

        Assert.True(allowed);
        Assert.Equal(200, status);
        Assert.Null(error);
    }

    [Fact]
    public void Evaluate_UnknownRole_IsBadRequestNamingTheRole()
    {
        var (allowed, status, error) = RoleAssignmentRules.Evaluate(["User"], true, ExistingRoles);

        Assert.False(allowed);
        Assert.Equal(400, status);
        Assert.Equal("Role 'User' does not exist.", error);
    }

    [Fact]
    public void Evaluate_OneUnknownRoleAmongKnownRoles_IsBadRequest()
    {
        var (allowed, status, error) = RoleAssignmentRules.Evaluate(["Viewer", "Ghost"], true, ExistingRoles);

        Assert.False(allowed);
        Assert.Equal(400, status);
        Assert.Contains("Ghost", error);
    }

    [Fact]
    public void Evaluate_NoRoles_IsBadRequest()
    {
        var (allowed, status, error) = RoleAssignmentRules.Evaluate([], true, ExistingRoles);

        Assert.False(allowed);
        Assert.Equal(400, status);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(nameof(IAdminUserService.CreateUserAsync))]
    [InlineData(nameof(IAdminUserService.UpdateUserAsync))]
    [InlineData(nameof(IAdminUserService.DeleteUserAsync))]
    [InlineData(nameof(IAdminUserService.ResetUserPasswordAsync))]
    public void AdminService_UserMutations_TakeCallerIsAdmin(string methodName)
    {
        var method = typeof(IAdminUserService).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Contains(method!.GetParameters(), p => p.Name == "callerIsAdmin" && p.ParameterType == typeof(bool));
    }

    [Fact]
    public void Register_RequiresUsersCreatePolicy()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Register));

        Assert.NotNull(method);
        var attributes = method!.GetCustomAttributes<AuthorizeAttribute>().ToList();
        Assert.Contains(attributes, a => a.Policy == Permissions.Users.Create);
        Assert.DoesNotContain(attributes, a => !string.IsNullOrEmpty(a.Roles));
    }
}
