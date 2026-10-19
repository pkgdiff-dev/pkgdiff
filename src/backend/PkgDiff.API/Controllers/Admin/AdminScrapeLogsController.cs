using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminScrapeLogsController : ControllerBase
{
    private readonly IAdminScrapeLogService _service;
    private readonly ILogger<AdminScrapeLogsController> _logger;

    public AdminScrapeLogsController(IAdminScrapeLogService service, ILogger<AdminScrapeLogsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("scrape-logs")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.ScrapeLogs.View)]
    public async Task<ActionResult<PaginatedResult<ScrapeLogInfo>>> GetScrapeLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? packageFilter = null)
    {
        pageSize = Math.Min(pageSize, 100);
        try
        {
            var result = await _service.GetScrapeLogsAsync(page, pageSize, packageFilter);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching scrape logs");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpGet("scrape-logs/summary")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.ScrapeLogs.View)]
    public async Task<ActionResult<ScrapeLogSummary>> GetScrapeLogSummary()
    {
        try
        {
            var result = await _service.GetScrapeLogSummaryAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching scrape log summary");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPut("scrape-logs/{id}/resolve")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.ScrapeLogs.Manage)]
    public async Task<IActionResult> ResolveScrapeLog(int id)
    {
        try
        {
            await _service.ResolveScrapeLogAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving scrape log {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPut("scrape-logs/resolve-package/{packageId}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.ScrapeLogs.Manage)]
    public async Task<IActionResult> ResolveAllScrapeLogsForPackage(string packageId)
    {
        try
        {
            await _service.ResolveAllScrapeLogsForPackageAsync(packageId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving scrape logs for {PackageId}", packageId);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("scrape-logs/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.ScrapeLogs.Manage)]
    public async Task<IActionResult> DeleteScrapeLog(int id)
    {
        try
        {
            await _service.DeleteScrapeLogAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting scrape log {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("scrape-logs/clear-resolved")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.ScrapeLogs.Manage)]
    public async Task<IActionResult> ClearResolvedScrapeLogs()
    {
        try
        {
            await _service.ClearResolvedScrapeLogsAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing resolved scrape logs");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }
}
