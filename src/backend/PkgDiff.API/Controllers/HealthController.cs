using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Health.View)]
public class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Get overall health status (quick check)
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<HealthStatus>> GetStatus()
    {
        var status = await _healthCheckService.GetOverallHealthAsync();
        return Ok(status);
    }

    /// <summary>
    /// Get detailed health information for all services
    /// </summary>
    [HttpGet("details")]
    public async Task<ActionResult<HealthDetails>> GetDetails()
    {
        var details = await _healthCheckService.GetDetailedHealthAsync();
        return Ok(details);
    }
}
