namespace PkgDiff.API.Services;

public interface IAdminRoleChecker
{
    Task<bool> IsActiveAdminAsync(string userId);
}
