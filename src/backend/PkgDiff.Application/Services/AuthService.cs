using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PkgDiff.Domain.Entities;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PkgDiff.Application.Services;

public class AuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole> roleManager,
        JwtSettings jwtSettings)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _jwtSettings = jwtSettings;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null)
        {
            // Hash anyway, so the answer for an unknown name takes as long as for a wrong password
            TimingHasher.VerifyHashedPassword(TimingUser, TimingHash.Value, request.Password);
            return null;
        }

        // No lockout: anyone could lock out a known admin name; guessing is throttled per address
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var token = await GenerateJwtToken(user);
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = new List<string>();

        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var claims = await _roleManager.GetClaimsAsync(role);
                permissions.AddRange(claims.Where(c => c.Type == "Permission").Select(c => c.Value));
            }
        }

        return new LoginResponse
        {
            Token = token,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? "User",
            Permissions = permissions.Distinct().ToList(),
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
    }

    public async Task<(int StatusCode, IReadOnlyList<string> Errors)> RegisterAsync(
        string username,
        string email,
        string password,
        string? requestedRole,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions)
    {
        var role = string.IsNullOrWhiteSpace(requestedRole) ? RoleAssignmentRules.DefaultRole : requestedRole.Trim();
        var existingRoles = _roleManager.Roles.Select(r => r.Name!).ToList();

        var (allowed, statusCode, error) = RoleAssignmentRules.Evaluate(new[] { role }, callerIsAdmin, existingRoles);
        if (allowed && !callerIsAdmin)
        {
            var granted = await RolePermissionLookup.OfRolesAsync(_roleManager, new[] { role });
            (allowed, statusCode, error) = RoleAssignmentRules.EvaluatePermissionSubset(
                granted, callerIsAdmin, callerPermissions, "You can only assign roles whose permissions you hold yourself.");
        }

        if (!allowed)
        {
            return (statusCode, new[] { error ?? "Role assignment is not allowed." });
        }

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return (400, createResult.Errors.Select(e => e.Description).ToList());
        }

        // Compensating delete keeps user + role creation all-or-nothing.
        IReadOnlyList<string> roleErrors;
        try
        {
            var roleResult = await _userManager.AddToRoleAsync(user, role);
            if (roleResult.Succeeded)
            {
                return (200, Array.Empty<string>());
            }
            roleErrors = roleResult.Errors.Select(e => e.Description).ToList();
        }
        catch (Exception)
        {
            roleErrors = new[] { "Role assignment failed." };
        }

        await _userManager.DeleteAsync(user);
        return (400, roleErrors.Count > 0 ? roleErrors : new[] { "Role assignment failed." });
    }

    public const string SecurityStampClaim = "sst";

    private static readonly PasswordHasher<ApplicationUser> TimingHasher = new();
    private static readonly ApplicationUser TimingUser = new();
    private static readonly Lazy<string> TimingHash = new(() => TimingHasher.HashPassword(TimingUser, Guid.NewGuid().ToString()));

    private async Task<string> GenerateJwtToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
            // Checked on every request: a password reset, a role change or a deletion ends the session
            new Claim(SecurityStampClaim, await _userManager.GetSecurityStampAsync(user) ?? string.Empty)
        };

        foreach (var roleName in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));

            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await _roleManager.GetClaimsAsync(role);
                foreach (var claim in roleClaims.Where(c => c.Type == "Permission"))
                {
                    claims.Add(claim);
                }
            }
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
