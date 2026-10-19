using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminRolesController : ControllerBase
{
    private readonly IAdminRoleService _service;
    private readonly ILogger<AdminRolesController> _logger;

    public AdminRolesController(IAdminRoleService service, ILogger<AdminRolesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("roles")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Roles.View)]
    public async Task<ActionResult<List<AdminRoleInfo>>> GetRoles()
    {
        try
        {
            var roles = await _service.GetRolesAsync();
            return Ok(roles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching roles");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("roles")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Roles.Create)]
    public async Task<ActionResult<AdminRoleInfo>> CreateRole([FromBody] CreateRoleRequest request)
    {
        try
        {
            var role = await _service.CreateRoleAsync(request);
            return Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating role");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("roles/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Roles.Delete)]
    public async Task<IActionResult> DeleteRole(string id)
    {
        try
        {
            await _service.DeleteRoleAsync(
                id,
                User.IsInRole(PkgDiff.Application.Services.RoleAssignmentRules.AdminRole),
                CallerPermissions());
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting role {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpGet("roles/{roleId}/permissions")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Roles.View)] // Viewing role perms is part of viewing roles
    public async Task<ActionResult<RolePermissionsDto>> GetRolePermissions(string roleId)
    {
        try
        {
            var result = await _service.GetRolePermissionsAsync(roleId);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching permissions for role {RoleId}", roleId);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPut("roles/{roleId}/permissions")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Roles.Edit)] // Editing perms is editing role
    public async Task<IActionResult> UpdateRolePermissions(string roleId, [FromBody] UpdateRolePermissionsRequest request)
    {
        if (roleId != request.RoleId) return BadRequest(new { error = "Role ID mismatch" });

        try
        {
            await _service.UpdateRolePermissionsAsync(
                request,
                User.IsInRole(PkgDiff.Application.Services.RoleAssignmentRules.AdminRole),
                CallerPermissions());
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating permissions for role {RoleId}", roleId);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    private List<string> CallerPermissions() =>
        User.FindAll(PkgDiff.Application.Services.RolePermissionLookup.PermissionClaimType).Select(c => c.Value).ToList();
}
