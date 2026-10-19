using Microsoft.AspNetCore.Identity;
using PkgDiff.Domain.Entities;

namespace PkgDiff.API.Services;

public class IdentityAdminRoleChecker : IAdminRoleChecker
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityAdminRoleChecker(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<bool> IsActiveAdminAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return false;

        if (await _userManager.IsLockedOutAsync(user))
            return false;

        return await _userManager.IsInRoleAsync(user, "Admin");
    }
}
