using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _service;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(IAdminUserService service, ILogger<AdminUsersController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("users")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.View)]
    public async Task<ActionResult<PaginatedResult<AdminUserInfo>>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        pageSize = Math.Min(pageSize, 100);
        try
        {
            var result = await _service.GetUsersAsync(page, pageSize, search);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching users");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpGet("users/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.View)]
    public async Task<ActionResult<AdminUserInfo>> GetUser(string id)
    {
        try
        {
            var user = await _service.GetUserByIdAsync(id);
            return Ok(user);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("users")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.Create)]
    public async Task<ActionResult<AdminUserInfo>> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var user = await _service.CreateUserAsync(request, User.IsInRole(PkgDiff.Application.Services.RoleAssignmentRules.AdminRole), CallerPermissions());
            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
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
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPut("users/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.Edit)]
    public async Task<ActionResult<AdminUserInfo>> UpdateUser(string id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var user = await _service.UpdateUserAsync(id, request, User.IsInRole(PkgDiff.Application.Services.RoleAssignmentRules.AdminRole), CallerPermissions());
            return Ok(user);
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
            _logger.LogError(ex, "Error updating user {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("users/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.Delete)]
    public async Task<IActionResult> DeleteUser(string id)
    {
        try
        {
            await _service.DeleteUserAsync(id, User.IsInRole(PkgDiff.Application.Services.RoleAssignmentRules.AdminRole), CallerPermissions());
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
            _logger.LogError(ex, "Error deleting user {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("users/{id}/password")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.Edit)]
    public async Task<IActionResult> ResetUserPassword(string id, [FromBody] ResetUserPasswordRequest request)
    {
        try
        {
            await _service.ResetUserPasswordAsync(id, request, User.IsInRole(PkgDiff.Application.Services.RoleAssignmentRules.AdminRole), CallerPermissions());
            return Ok(new { message = "Password reset successfully" });
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
            _logger.LogError(ex, "Error resetting password for user {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    private List<string> CallerPermissions() =>
        User.FindAll(PkgDiff.Application.Services.RolePermissionLookup.PermissionClaimType).Select(c => c.Value).ToList();
}
