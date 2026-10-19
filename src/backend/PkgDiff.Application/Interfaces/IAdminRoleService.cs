using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminRoleService
{
    Task<List<AdminRoleInfo>> GetRolesAsync();
    Task<AdminRoleInfo> CreateRoleAsync(CreateRoleRequest request);
    Task DeleteRoleAsync(string id, bool callerIsAdmin, IReadOnlyCollection<string> callerPermissions);
    Task<RolePermissionsDto> GetRolePermissionsAsync(string roleId);
    Task UpdateRolePermissionsAsync(UpdateRolePermissionsRequest request, bool callerIsAdmin, IReadOnlyCollection<string> callerPermissions);
}
