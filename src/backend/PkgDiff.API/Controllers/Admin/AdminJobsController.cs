using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminJobsController : ControllerBase
{
    private readonly IAdminJobService _service;
    private readonly ILogger<AdminJobsController> _logger;

    public AdminJobsController(IAdminJobService service, ILogger<AdminJobsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("jobs")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Jobs.View)]
    public async Task<ActionResult<JobsOverview>> GetJobs()
    {
        try
        {
            var result = await _service.GetJobsOverviewAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching jobs overview");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("jobs/{jobId}/trigger")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Jobs.Manage)]
    public async Task<IActionResult> TriggerJob(string jobId)
    {
        try
        {
            await _service.TriggerJobAsync(jobId);
            return Ok(new { message = "Job triggered successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering job {JobId}", jobId);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("jobs/{jobId}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Jobs.Manage)]
    public async Task<IActionResult> DeleteJob(string jobId)
    {
        try
        {
            await _service.DeleteJobAsync(jobId);
            return Ok(new { message = "Job deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting job {JobId}", jobId);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }
}
