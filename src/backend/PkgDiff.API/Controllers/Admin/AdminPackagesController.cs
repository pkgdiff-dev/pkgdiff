using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminPackagesController : ControllerBase
{
    private readonly IAdminPackageService _service;
    private readonly ILogger<AdminPackagesController> _logger;

    public AdminPackagesController(IAdminPackageService service, ILogger<AdminPackagesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("packages")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Packages.View)]
    public async Task<ActionResult<PaginatedResult<AdminPackageInfo>>> GetPackages(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        pageSize = Math.Min(pageSize, 100);
        try
        {
            var result = await _service.GetPackagesAsync(page, pageSize, search);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching packages");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("packages/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Packages.Delete)]
    public async Task<IActionResult> DeletePackage(int id)
    {
        try
        {
            await _service.DeletePackageAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting package {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpGet("registries")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Registries.View)]
    public async Task<ActionResult<List<AdminRegistryInfo>>> GetRegistries()
    {
        try
        {
            var result = await _service.GetRegistriesAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching registries");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("registries")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Registries.Create)]
    public async Task<ActionResult<AdminRegistryInfo>> CreateRegistry([FromBody] RegistryCreateRequest request)
    {
        try
        {
            var result = await _service.CreateRegistryAsync(request);
            return Created($"api/admin/registries/{result.Id}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating registry");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPut("registries/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Registries.Edit)]
    public async Task<ActionResult<AdminRegistryInfo>> UpdateRegistry(int id, [FromBody] RegistryUpdateRequest request)
    {
        try
        {
            var result = await _service.UpdateRegistryAsync(id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating registry {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpDelete("registries/{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Registries.Delete)]
    public async Task<IActionResult> DeleteRegistry(int id)
    {
        try
        {
            await _service.DeleteRegistryAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting registry {Id}", id);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    [HttpPost("registries/reorder")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Registries.Edit)]
    public async Task<IActionResult> ReorderRegistries([FromBody] List<RegistryReorderRequest> requests)
    {
        try
        {
            await _service.ReorderRegistriesAsync(requests);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering registries");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }
}
