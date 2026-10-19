using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminUserService : IAdminUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminUserService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<PaginatedResult<AdminUserInfo>> GetUsersAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        var users = _userManager.Users;

        if (!string.IsNullOrWhiteSpace(search))
        {
            users = users.Where(u =>
                u.UserName!.Contains(search) ||
                u.Email!.Contains(search));
        }

        var total = users.Count();
        var userList = users
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new List<AdminUserInfo>();
        foreach (var user in userList)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new AdminUserInfo
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Roles = roles.ToList(),
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                EmailConfirmed = user.EmailConfirmed,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd?.UtcDateTime
            });
        }

        return new PaginatedResult<AdminUserInfo>
        {
            Data = result,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<AdminUserInfo> GetUserByIdAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) throw new KeyNotFoundException($"User with id {id} not found");

        var roles = await _userManager.GetRolesAsync(user);

        return new AdminUserInfo
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            EmailConfirmed = user.EmailConfirmed,
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd?.UtcDateTime
        };
    }

    public async Task<AdminUserInfo> CreateUserAsync(
        CreateUserRequest request,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions)
    {
        ThrowIfRoleAssignmentRejected(RoleAssignmentRules.Evaluate(request.Roles, callerIsAdmin, GetExistingRoleNames()));
        await ThrowIfRolesExceedCallerAsync(request.Roles, callerIsAdmin, callerPermissions);

        var user = new ApplicationUser
        {
            UserName = request.Username,
            Email = request.Email,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        // Compensating delete keeps user + role creation all-or-nothing.
        string? roleError = null;
        try
        {
            var roleResult = await _userManager.AddToRolesAsync(user, request.Roles);
            if (!roleResult.Succeeded)
            {
                roleError = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            }
        }
        catch (Exception)
        {
            roleError = "Role assignment failed.";
        }

        if (roleError != null)
        {
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(roleError) ? "Role assignment failed." : roleError);
        }

        return new AdminUserInfo
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Roles = request.Roles,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            EmailConfirmed = user.EmailConfirmed,
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd?.UtcDateTime
        };
    }

    public async Task<AdminUserInfo> UpdateUserAsync(
        string id,
        UpdateUserRequest request,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) throw new KeyNotFoundException($"User with id {id} not found");

        // An Admin's email is part of their account recovery surface, so any edit of an Admin is Admin-only.
        await ThrowIfNonAdminTargetsAdminAsync(user, callerIsAdmin, "Only administrators can edit an administrator.");
        await ThrowIfTargetExceedsCallerAsync(user, callerIsAdmin, callerPermissions, "You cannot manage a user with permissions you do not hold.");

        var currentRoles = await _userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Except(request.Roles).ToList();
        var rolesToAdd = request.Roles.Except(currentRoles).ToList();

        // Rules are checked before anything (email included) is changed.
        if (rolesToAdd.Any())
        {
            ThrowIfRoleAssignmentRejected(RoleAssignmentRules.Evaluate(rolesToAdd, callerIsAdmin, GetExistingRoleNames()));
            await ThrowIfRolesExceedCallerAsync(rolesToAdd, callerIsAdmin, callerPermissions);
        }

        if (rolesToRemove.Any(r => string.Equals(r, RoleAssignmentRules.AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            var adminCount = (await _userManager.GetUsersInRoleAsync(RoleAssignmentRules.AdminRole)).Count;
            ThrowIfRoleAssignmentRejected(RoleAssignmentRules.EvaluateRemoval(rolesToRemove, callerIsAdmin, adminCount));
        }

        user.Email = request.Email;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", updateResult.Errors.Select(e => e.Description)));
        }

        if (rolesToRemove.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", removeResult.Errors.Select(e => e.Description)));
            }
        }

        if (rolesToAdd.Any())
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", addResult.Errors.Select(e => e.Description)));
            }
        }

        // Tokens carry the roles they were issued with: new ones have to be fetched
        if (rolesToRemove.Any() || rolesToAdd.Any())
            await _userManager.UpdateSecurityStampAsync(user);

        return new AdminUserInfo
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Roles = request.Roles,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            EmailConfirmed = user.EmailConfirmed,
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd?.UtcDateTime
        };
    }

    private List<string> GetExistingRoleNames()
    {
        return _roleManager.Roles.Select(r => r.Name!).ToList();
    }

    private async Task ThrowIfNonAdminTargetsAdminAsync(ApplicationUser target, bool callerIsAdmin, string message)
    {
        if (!callerIsAdmin && await _userManager.IsInRoleAsync(target, RoleAssignmentRules.AdminRole))
        {
            throw new UnauthorizedAccessException(message);
        }
    }

    private async Task ThrowIfRolesExceedCallerAsync(
        IEnumerable<string> roles,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions)
    {
        if (callerIsAdmin)
        {
            return;
        }

        var granted = await RolePermissionLookup.OfRolesAsync(_roleManager, roles);
        ThrowIfRoleAssignmentRejected(RoleAssignmentRules.EvaluatePermissionSubset(
            granted, callerIsAdmin, callerPermissions, "You can only assign roles whose permissions you hold yourself."));
    }

    // Taking over (password reset) or re-pointing (email) an account is equivalent to holding its permissions
    private async Task ThrowIfTargetExceedsCallerAsync(
        ApplicationUser target,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions,
        string message)
    {
        if (callerIsAdmin)
        {
            return;
        }

        var targetPermissions = await RolePermissionLookup.OfUserAsync(_userManager, _roleManager, target);
        ThrowIfRoleAssignmentRejected(RoleAssignmentRules.EvaluatePermissionSubset(
            targetPermissions, callerIsAdmin, callerPermissions, message));
    }

    private static void ThrowIfRoleAssignmentRejected((bool Allowed, int StatusCode, string? Error) outcome)
    {
        if (outcome.Allowed)
        {
            return;
        }

        var message = outcome.Error ?? "Role assignment is not allowed.";
        if (outcome.StatusCode == 403)
        {
            throw new UnauthorizedAccessException(message);
        }

        throw new InvalidOperationException(message);
    }

    public async Task DeleteUserAsync(string id, bool callerIsAdmin, IReadOnlyCollection<string>? callerPermissions = null)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) throw new KeyNotFoundException($"User with id {id} not found");

        // The same rule as editing and resetting: a non-Admin cannot remove someone who holds more than they do
        await ThrowIfTargetExceedsCallerAsync(user, callerIsAdmin, callerPermissions ?? Array.Empty<string>(),
            "You cannot delete a user with permissions you do not hold.");

        if (await _userManager.IsInRoleAsync(user, RoleAssignmentRules.AdminRole))
        {
            var adminCount = (await _userManager.GetUsersInRoleAsync(RoleAssignmentRules.AdminRole)).Count;
            ThrowIfRoleAssignmentRejected(RoleAssignmentRules.EvaluateDeletion(targetIsAdmin: true, callerIsAdmin, adminCount));
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task ResetUserPasswordAsync(
        string id,
        ResetUserPasswordRequest request,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) throw new KeyNotFoundException($"User with id {id} not found");

        // Resetting an Admin's password is an account takeover, so it needs the same Admin-only rule as granting Admin.
        await ThrowIfNonAdminTargetsAdminAsync(user, callerIsAdmin, "Only administrators can reset an administrator's password.");
        await ThrowIfTargetExceedsCallerAsync(user, callerIsAdmin, callerPermissions, "You cannot reset the password of a user with permissions you do not hold.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
