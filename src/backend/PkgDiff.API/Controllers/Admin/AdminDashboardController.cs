using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _service;
    private readonly ILogger<AdminDashboardController> _logger;

    public AdminDashboardController(IAdminDashboardService service, ILogger<AdminDashboardController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ───── Dashboard ─────

    [HttpGet("dashboard")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Health.View)]
    public async Task<ActionResult<DashboardStats>> GetDashboard()
    {
        try
        {
            var stats = await _service.GetDashboardStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dashboard stats");
            return StatusCode(500, new { error = "An error occurred while fetching dashboard stats" });
        }
    }

    // ───── Search History ─────

    [HttpGet("search-history")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ViewLogs)]
    public async Task<ActionResult<PaginatedResult<RecentSearch>>> GetSearchHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Min(pageSize, 100);
        try
        {
            var result = await _service.GetSearchHistoryAsync(page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching search history");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("search-history/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ManageLogs)]
    public async Task<IActionResult> DeleteSearchHistory(int id)
    {
        try
        {
            await _service.DeleteSearchHistoryAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting search history {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("search-history")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ManageLogs)]
    public async Task<IActionResult> ClearSearchHistory()
    {
        try
        {
            await _service.ClearSearchHistoryAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing search history");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }
}
