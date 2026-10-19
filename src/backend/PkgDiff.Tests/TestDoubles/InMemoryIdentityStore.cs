using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Tests.TestDoubles;

public sealed class InMemoryIdentityStore :
    IUserRoleStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IQueryableRoleStore<IdentityRole>,
    IRoleClaimStore<IdentityRole>,
    IUserSecurityStampStore<ApplicationUser>
{
    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    private readonly Dictionary<string, List<System.Security.Claims.Claim>> _roleClaims = new();
    private readonly List<ApplicationUser> _users = new();
    private readonly List<IdentityRole> _roles = new();
    private readonly Dictionary<string, HashSet<string>> _userRoles = new();

    public IQueryable<IdentityRole> Roles => _roles.AsQueryable();

    public UserManager<ApplicationUser> CreateUserManager() =>
        new(
            this,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

    public RoleManager<IdentityRole> CreateRoleManager() =>
        new(
            this,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

    public void AddRole(string name) =>
        _roles.Add(new IdentityRole(name) { NormalizedName = name.ToUpperInvariant() });

    public ApplicationUser AddUser(string userName, params string[] roles)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName + "@example.test"
        };
        _users.Add(user);
        _userRoles[user.Id] = new HashSet<string>(roles.Select(r => r.ToUpperInvariant()));
        return user;
    }

    public IReadOnlyCollection<string> RolesOf(ApplicationUser user) =>
        _roles.Where(r => _userRoles[user.Id].Contains(r.NormalizedName!)).Select(r => r.Name!).ToList();

    public void Dispose()
    {
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _users.Add(user);
        _userRoles[user.Id] = new HashSet<string>();
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _users.Remove(user);
        _userRoles.Remove(user.Id);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Id == userId));

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(_users.FirstOrDefault(u => u.NormalizedUserName == normalizedUserName));

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash is not null);

    // Role names arrive normalized
    public Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        _userRoles[user.Id].Add(roleName);
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        _userRoles[user.Id].Remove(roleName);
        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<IList<string>>(RolesOf(user).ToList());

    public Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken) =>
        Task.FromResult(_userRoles[user.Id].Contains(roleName));

    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        Task.FromResult<IList<ApplicationUser>>(_users.Where(u => _userRoles[u.Id].Contains(roleName)).ToList());

    public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        _roles.Add(role);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        _roles.Remove(role);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id);
    public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);

    public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<string> PermissionsOf(string roleName) =>
        _roleClaims.TryGetValue(RoleNamed(roleName).Id, out var claims)
            ? claims.Where(c => c.Type == "Permission").Select(c => c.Value).ToList()
            : [];

    public IdentityRole RoleNamed(string roleName) => _roles.Single(r => r.Name == roleName);

    public Task<IList<System.Security.Claims.Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken = default) =>
        Task.FromResult<IList<System.Security.Claims.Claim>>(
            _roleClaims.TryGetValue(role.Id, out var claims) ? claims.ToList() : new List<System.Security.Claims.Claim>());

    public Task AddClaimAsync(IdentityRole role, System.Security.Claims.Claim claim, CancellationToken cancellationToken = default)
    {
        if (!_roleClaims.TryGetValue(role.Id, out var claims))
        {
            claims = new List<System.Security.Claims.Claim>();
            _roleClaims[role.Id] = claims;
        }

        claims.Add(claim);
        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(IdentityRole role, System.Security.Claims.Claim claim, CancellationToken cancellationToken = default)
    {
        if (_roleClaims.TryGetValue(role.Id, out var claims))
        {
            claims.RemoveAll(c => c.Type == claim.Type && c.Value == claim.Value);
        }

        return Task.CompletedTask;
    }

    Task<IdentityRole?> IRoleStore<IdentityRole>.FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.FirstOrDefault(r => r.Id == roleId));

    Task<IdentityRole?> IRoleStore<IdentityRole>.FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.FirstOrDefault(r => r.NormalizedName == normalizedRoleName));
}
