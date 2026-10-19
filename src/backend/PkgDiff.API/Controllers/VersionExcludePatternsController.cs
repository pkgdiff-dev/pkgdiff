using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/admin/version-exclude-patterns")]
[Authorize]
public class VersionExcludePatternsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IVersionFilterService _versionFilterService;

    public VersionExcludePatternsController(IUnitOfWork unitOfWork, IVersionFilterService versionFilterService)
    {
        _unitOfWork = unitOfWork;
        _versionFilterService = versionFilterService;
    }

    /// <summary>
    /// Get all version exclude patterns
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.View)]
    public async Task<ActionResult<IEnumerable<VersionExcludePattern>>> GetAll()
    {
        var patterns = await _unitOfWork.VersionExcludePatterns.GetAllAsync();
        return Ok(patterns.OrderBy(p => p.Id));
    }

    /// <summary>
    /// Get a specific pattern by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.View)]
    public async Task<ActionResult<VersionExcludePattern>> GetById(int id)
    {
        var pattern = await _unitOfWork.VersionExcludePatterns.GetByIdAsync(id);
        if (pattern == null)
            return NotFound(new { message = $"Pattern with ID {id} not found" });

        return Ok(pattern);
    }

    /// <summary>
    /// Create a new exclude pattern
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.Create)]
    public async Task<ActionResult<VersionExcludePattern>> Create([FromBody] CreatePatternRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Pattern))
            return BadRequest(new { message = "Pattern is required" });

        var pattern = new VersionExcludePattern
        {
            Pattern = request.Pattern.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            IsActive = request.IsActive
        };

        await _unitOfWork.VersionExcludePatterns.AddAsync(pattern);
        await _unitOfWork.SaveChangesAsync();

        // Refresh cache
        await _versionFilterService.RefreshPatternsAsync();

        return CreatedAtAction(nameof(GetById), new { id = pattern.Id }, pattern);
    }

    /// <summary>
    /// Update an existing pattern
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.Edit)]
    public async Task<ActionResult<VersionExcludePattern>> Update(int id, [FromBody] UpdatePatternRequest request)
    {
        var pattern = await _unitOfWork.VersionExcludePatterns.GetByIdAsync(id);
        if (pattern == null)
            return NotFound(new { message = $"Pattern with ID {id} not found" });

        if (!string.IsNullOrWhiteSpace(request.Pattern))
            pattern.Pattern = request.Pattern.Trim();

        if (request.Description != null)
            pattern.Description = request.Description.Trim();

        if (request.IsActive.HasValue)
            pattern.IsActive = request.IsActive.Value;

        await _unitOfWork.VersionExcludePatterns.UpdateAsync(pattern);
        await _unitOfWork.SaveChangesAsync();

        // Refresh cache
        await _versionFilterService.RefreshPatternsAsync();

        return Ok(pattern);
    }

    /// <summary>
    /// Delete a pattern
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.Delete)]
    public async Task<ActionResult> Delete(int id)
    {
        var pattern = await _unitOfWork.VersionExcludePatterns.GetByIdAsync(id);
        if (pattern == null)
            return NotFound(new { message = $"Pattern with ID {id} not found" });

        await _unitOfWork.VersionExcludePatterns.DeleteAsync(pattern);
        await _unitOfWork.SaveChangesAsync();

        // Refresh cache
        await _versionFilterService.RefreshPatternsAsync();

        return NoContent();
    }

    /// <summary>
    /// Toggle pattern active status
    /// </summary>
    [HttpPatch("{id}/toggle")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.Edit)]
    public async Task<ActionResult<VersionExcludePattern>> ToggleActive(int id)
    {
        var pattern = await _unitOfWork.VersionExcludePatterns.GetByIdAsync(id);
        if (pattern == null)
            return NotFound(new { message = $"Pattern with ID {id} not found" });

        pattern.IsActive = !pattern.IsActive;
        await _unitOfWork.VersionExcludePatterns.UpdateAsync(pattern);
        await _unitOfWork.SaveChangesAsync();

        // Refresh cache
        await _versionFilterService.RefreshPatternsAsync();

        return Ok(pattern);
    }

    /// <summary>
    /// Preview: Test pattern against sample versions
    /// Returns 3 example versions that would be excluded
    /// </summary>
    [HttpPost("preview")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.View)] // View might be enough, but it's a tool for editing
    public ActionResult<PreviewResponse> Preview([FromBody] PreviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Pattern))
            return BadRequest(new { message = "Pattern is required" });

        // Sample versions for testing
        var sampleVersions = new[]
        {
            "1.0.0",
            "1.0.1-dev-123",
            "1.0.1-dev-456",
            "1.1.0-ci-789",
            "1.1.0-ci-012",
            "1.2.0-build-345",
            "1.2.0-build-678",
            "1.3.0-nightly-20240101",
            "1.3.0-nightly-20240102",
            "1.4.0-snapshot",
            "1.4.0-snapshot-2",
            "1.5.0-local",
            "1.5.0-local-debug",
            "1.6.0-test-abc",
            "1.6.0-test-def",
            "2.0.0-beta-1",
            "2.0.0-beta-2",
            "2.0.0-rc.1",
            "2.0.0",
            "2.1.0-alpha.1"
        };

        var matchedVersions = sampleVersions
            .Where(v => v.Contains(request.Pattern, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        return Ok(new PreviewResponse(
            request.Pattern,
            matchedVersions,
            matchedVersions.Count,
            matchedVersions.Count == 0 
                ? "No sample versions match this pattern" 
                : $"Found {matchedVersions.Count} matching version(s)"
        ));
    }
}

// DTOs
public record CreatePatternRequest(string Pattern, string? Description, bool IsActive = true);
public record UpdatePatternRequest(string? Pattern, string? Description, bool? IsActive);
public record PreviewRequest(string Pattern);
public record PreviewResponse(string Pattern, List<string> MatchedVersions, int MatchCount, string Message);
