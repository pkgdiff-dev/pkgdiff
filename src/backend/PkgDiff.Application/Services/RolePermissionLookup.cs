using Microsoft.AspNetCore.Identity;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Application.Services;

public static class RolePermissionLookup
{
    public const string PermissionClaimType = "Permission";

    public static async Task<HashSet<string>> OfRolesAsync(RoleManager<IdentityRole> roleManager, IEnumerable<string> roleNames)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleName in roleNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role == null)
                continue;

            var claims = await roleManager.GetClaimsAsync(role);
            permissions.UnionWith(claims.Where(c => c.Type == PermissionClaimType).Select(c => c.Value));
        }

        return permissions;
    }

    public static async Task<HashSet<string>> OfUserAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return await OfRolesAsync(roleManager, roles);
    }
}
