using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    // All visitors together; each audit takes a job worker
    private const int MaxActiveAudits = 20;
    private const long MaxRequestBytes = 2 * 1024 * 1024;

    private readonly IManifestParser _parser;
    private readonly IDependencyAuditService _audits;
    private readonly ILogger<AuditController> _logger;

    public AuditController(IManifestParser parser, IDependencyAuditService audits, ILogger<AuditController> logger)
    {
        _parser = parser;
        _audits = audits;
        _logger = logger;
    }

    [HttpPost("parse")]
    [RequestSizeLimit(MaxRequestBytes)]
    public ActionResult<ManifestParseResult> Parse([FromBody] ManifestParseRequest request)
    {
        try
        {
            return Ok(_parser.Parse(request.Registry, request.Content, request.FileName));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading a {Registry} dependency file", request.Registry);
            return StatusCode(500, new { error = "The file could not be read." });
        }
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<ActionResult<StartAuditResponse>> Start([FromBody] StartAuditRequest request)
    {
        try
        {
            var active = await _audits.CountActiveAsync();
            if (active >= MaxActiveAudits)
                return StatusCode(429, new { error = "Many audits are running right now. Please try again in a minute.", activeCount = active });

            var id = await _audits.StartAsync(request);
            return Ok(new StartAuditResponse { Id = id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting a {Registry} dependency audit", request.Registry);
            return StatusCode(500, new { error = "The audit could not be started." });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditReport>> Get(Guid id)
    {
        try
        {
            var report = await _audits.GetAsync(id);
            return report is null ? NotFound(new { error = "This report does not exist or has expired." }) : Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading dependency audit {AuditId}", id);
            return StatusCode(500, new { error = "The report could not be loaded." });
        }
    }
}
