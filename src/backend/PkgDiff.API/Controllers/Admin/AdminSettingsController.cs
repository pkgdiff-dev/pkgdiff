using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminSettingsController : ControllerBase
{
    private readonly IAdminSettingsService _service;
    private readonly ILogger<AdminSettingsController> _logger;

    public AdminSettingsController(IAdminSettingsService service, ILogger<AdminSettingsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("settings")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Settings.View)]
    public async Task<ActionResult<List<SystemSettingDto>>> GetSettings()
    {
        try
        {
            var result = await _service.GetSettingsAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching settings");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPut("settings/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Settings.Edit)]
    public async Task<ActionResult<SystemSettingDto>> UpdateSetting(int id, [FromBody] UpdateSettingRequest request)
    {
        try
        {
            var result = await _service.UpdateSettingAsync(id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating setting {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }
}
