using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminUserService
{
    Task<PaginatedResult<AdminUserInfo>> GetUsersAsync(int page = 1, int pageSize = 20, string? search = null);
    Task<AdminUserInfo> GetUserByIdAsync(string id);
    Task<AdminUserInfo> CreateUserAsync(CreateUserRequest request, bool callerIsAdmin, IReadOnlyCollection<string> callerPermissions);
    Task<AdminUserInfo> UpdateUserAsync(string id, UpdateUserRequest request, bool callerIsAdmin, IReadOnlyCollection<string> callerPermissions);
    Task DeleteUserAsync(string id, bool callerIsAdmin, IReadOnlyCollection<string>? callerPermissions = null);
    Task ResetUserPasswordAsync(string id, ResetUserPasswordRequest request, bool callerIsAdmin, IReadOnlyCollection<string> callerPermissions);
}
