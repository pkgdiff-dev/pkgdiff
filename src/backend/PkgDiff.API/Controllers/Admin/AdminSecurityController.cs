using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminSecurityController : ControllerBase
{
    private readonly IAdminSecurityService _service;
    private readonly ILogger<AdminSecurityController> _logger;

    public AdminSecurityController(IAdminSecurityService service, ILogger<AdminSecurityController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("security/request-logs")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ViewLogs)]
    public async Task<ActionResult<PaginatedResult<RequestLogInfo>>> GetRequestLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? ipFilter = null)
    {
        pageSize = Math.Min(pageSize, 100);
        try
        {
            var result = await _service.GetRequestLogsAsync(page, pageSize, ipFilter);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching request logs");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpGet("security/blocked-ips")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ManageIps)]
    public async Task<ActionResult<PaginatedResult<BlockedIpInfo>>> GetBlockedIps(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Min(pageSize, 100);
        try
        {
            var result = await _service.GetBlockedIpsAsync(page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blocked IPs");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("security/block-ip")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ManageIps)]
    public async Task<ActionResult<BlockedIpInfo>> BlockIp([FromBody] BlockIpRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "admin";
            var result = await _service.BlockIpAsync(request, username);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error blocking IP {IpAddress}", request.IpAddress);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("security/blocked-ips/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ManageIps)]
    public async Task<IActionResult> UnblockIp(int id)
    {
        try
        {
            await _service.UnblockIpAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unblocking IP {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    // ViewLogs alone is too broad (the seeded Viewer role has it), so ManageIps is required too
    [HttpGet("security/ip-statistics")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ViewLogs)]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Security.ManageIps)]
    public async Task<ActionResult<List<IpStatistics>>> GetIpStatistics([FromQuery] int topCount = 20)
    {
        topCount = Math.Min(topCount, 100);
        try
        {
            var result = await _service.GetIpStatisticsAsync(topCount);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching IP statistics");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }
}
