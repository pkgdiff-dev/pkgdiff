using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.API.Services;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/admin/jobs")]
[Authorize]
public class HangfireDashboardController : ControllerBase
{
    private readonly HangfireDashboardAccess _access;
    private readonly IAdminRoleChecker _adminRoleChecker;
    private readonly ILogger<HangfireDashboardController> _logger;

    public HangfireDashboardController(
        HangfireDashboardAccess access,
        IAdminRoleChecker adminRoleChecker,
        ILogger<HangfireDashboardController> logger)
    {
        _access = access;
        _adminRoleChecker = adminRoleChecker;
        _logger = logger;
    }

    [HttpPost("dashboard-ticket")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Jobs.Manage)]
    public async Task<IActionResult> CreateDashboardTicket()
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { error = "Authentication required." });

            // Jobs.Manage can be granted to non-admin roles; the dashboard itself is Admin-only.
            if (!await _adminRoleChecker.IsActiveAdminAsync(userId))
                return StatusCode(403, new { error = "Only administrators can open the Hangfire dashboard." });

            return Ok(new { url = "/hangfire?ticket=" + Uri.EscapeDataString(_access.CreateTicket(userId)) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating a Hangfire dashboard ticket");
            return StatusCode(500, new { error = "An error occurred while opening the Hangfire dashboard." });
        }
    }
}
