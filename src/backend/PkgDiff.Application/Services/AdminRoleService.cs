using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminRoleService : IAdminRoleService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminRoleService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<List<AdminRoleInfo>> GetRolesAsync()
    {
        var roles = _roleManager.Roles.ToList();
        var result = new List<AdminRoleInfo>();

        foreach (var role in roles)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            result.Add(new AdminRoleInfo
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                UserCount = usersInRole.Count
            });
        }

        return result.OrderBy(r => r.Name).ToList();
    }

    public async Task<AdminRoleInfo> CreateRoleAsync(CreateRoleRequest request)
    {
        var role = new IdentityRole(request.Name);
        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return new AdminRoleInfo
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            UserCount = 0
        };
    }

    public async Task DeleteRoleAsync(string id, bool callerIsAdmin, IReadOnlyCollection<string> callerPermissions)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null) throw new KeyNotFoundException($"Role with id {id} not found");

        if (role.Name == "Admin")
        {
            throw new InvalidOperationException("The 'Admin' role is a system role and cannot be deleted.");
        }

        // Deleting a role revokes its permissions from every member: a non-Admin may do that only for
        // permissions they hold, as when editing the role
        if (!callerIsAdmin)
        {
            var rolePermissions = (await _roleManager.GetClaimsAsync(role)).Where(c => c.Type == "Permission").Select(c => c.Value);
            if (rolePermissions.Any(p => !callerPermissions.Contains(p, StringComparer.Ordinal)))
            {
                throw new UnauthorizedAccessException("You cannot delete a role with permissions you do not hold.");
            }
        }

        var members = await _userManager.GetUsersInRoleAsync(role.Name!);
        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        await EndSessionsAsync(members);
    }

    // Tokens carry the permissions of their roles: the members of a changed role sign in again
    private async Task EndSessionsAsync(IEnumerable<ApplicationUser> members)
    {
        foreach (var member in members)
            await _userManager.UpdateSecurityStampAsync(member);
    }

    public async Task<RolePermissionsDto> GetRolePermissionsAsync(string roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null) throw new KeyNotFoundException($"Role with id {roleId} not found");

        var claims = await _roleManager.GetClaimsAsync(role);
        var rolePermissions = claims.Where(c => c.Type == "Permission").Select(c => c.Value).ToHashSet();
        var allPermissions = PkgDiff.Shared.Constants.Permissions.GetAllPermissions();

        var permissionDtos = allPermissions.Select(p => new PermissionDto
        {
            Name = p,
            Description = p.Split('.').Last(),
            Group = p.Split('.').Length > 2 ? p.Split('.')[1] : "General",
            IsSelected = rolePermissions.Contains(p)
        }).ToList();

        return new RolePermissionsDto
        {
            RoleId = role.Id,
            RoleName = role.Name ?? string.Empty,
            Permissions = permissionDtos
        };
    }

    public async Task UpdateRolePermissionsAsync(
        UpdateRolePermissionsRequest request,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions)
    {
        var role = await _roleManager.FindByIdAsync(request.RoleId);
        if (role == null) throw new KeyNotFoundException($"Role with id {request.RoleId} not found");

        if (string.Equals(role.Name, RoleAssignmentRules.AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The 'Admin' role has full system access and cannot be modified.");
        }

        var knownPermissions = PkgDiff.Shared.Constants.Permissions.GetAllPermissions().ToHashSet(StringComparer.Ordinal);
        var requestedPermissions = request.Permissions.Distinct(StringComparer.Ordinal).ToList();
        var unknownPermissions = requestedPermissions.Where(p => !knownPermissions.Contains(p)).ToList();
        if (unknownPermissions.Count > 0)
        {
            throw new InvalidOperationException($"Unknown permission(s): {string.Join(", ", unknownPermissions)}");
        }

        var currentClaims = await _roleManager.GetClaimsAsync(role);
        var permissionClaims = currentClaims.Where(c => c.Type == "Permission").ToList();

        // Permission policies pass on the claim alone, so a non-Admin may only grant or revoke what they hold;
        // otherwise Roles.Edit would let them hand their own role every permission
        if (!callerIsAdmin)
        {
            var currentPermissions = permissionClaims.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
            var changedPermissions = requestedPermissions.Where(p => !currentPermissions.Contains(p))
                .Concat(currentPermissions.Where(p => !requestedPermissions.Contains(p, StringComparer.Ordinal)));
            if (changedPermissions.Any(p => !callerPermissions.Contains(p, StringComparer.Ordinal)))
            {
                throw new UnauthorizedAccessException("You can only grant or revoke permissions that you hold yourself.");
            }
        }

        // Remove existing permission claims
        foreach (var claim in permissionClaims)
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        // Add new permission claims
        foreach (var permission in requestedPermissions)
        {
            await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim("Permission", permission));
        }

        await EndSessionsAsync(await _userManager.GetUsersInRoleAsync(role.Name!));
    }
}
