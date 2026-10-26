using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Shared.DTOs;
using PkgDiff.API.Extensions;
using PkgDiff.API.Filters;
using PkgDiff.API.Services;
using Microsoft.AspNetCore.DataProtection;
namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PackageController : ControllerBase
{
    private const int MaxConcurrentAnalyses = 10;

    private const int MaxConcurrentAnalysesPerAddress = 2;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> AnalysesPerAddress = new();

    private const int ClientClosedRequest = 499;

    private readonly IPackageAnalysisService _analysisService;
    private readonly ILiveUpdateService _liveUpdates;
    private readonly IJobScheduler _jobs;
    private readonly IExportService _exportService;
    private readonly IWorkTrackerService _workTracker;
    private readonly ILogger<PackageController> _logger;
    private readonly IDistributedCache _distributedCache;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IPackageTrustService _trust;
    private readonly IScrapeBudget _scrapeBudget;
    private readonly IPackageCacheService _cache;
    private readonly IMemoryCache _memory;

    public PackageController(
        IPackageAnalysisService analysisService,
        ILiveUpdateService liveUpdates,
        IJobScheduler jobs,
        IExportService exportService,
        IWorkTrackerService workTracker,
        ILogger<PackageController> logger,
        IDistributedCache distributedCache,
        IDataProtectionProvider dataProtection,
        IPackageTrustService trust,
        IScrapeBudget scrapeBudget,
        IPackageCacheService cache,
        IMemoryCache memory)
    {
        _scrapeBudget = scrapeBudget;
        _cache = cache;
        _memory = memory;
        _analysisService = analysisService;
        _liveUpdates = liveUpdates;
        _jobs = jobs;
        _exportService = exportService;
        _workTracker = workTracker;
        _logger = logger;
        _distributedCache = distributedCache;
        _dataProtection = dataProtection;
        _trust = trust;
    }

    // A request that joins an analysis already running is let through: it starts no new work
    private ObjectResult? RejectWhenBusy(AnalyzeRequest request)
    {
        var activeCount = _workTracker.GetActiveCount(WorkKeys.AnalysisPrefix);
        if (activeCount < MaxConcurrentAnalyses || _workTracker.IsRunning(WorkKeys.Analysis(request)))
            return null;

        return StatusCode(429, new
        {
            error = "Server is busy. Too many analyses in progress. Please try again in a moment.",
            activeCount
        });
    }

    [HttpPost("analyze")]
    [ValidatePackageRoute]
    public async Task<ActionResult<AnalysisResult>> AnalyzePackage([FromBody] AnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (RejectWhenBusy(request) is { } busy)
            return busy;

        var address = HttpContext.GetClientIpAddress();
        var joinsRunning = _workTracker.IsRunning(WorkKeys.Analysis(request));
        if (!joinsRunning && AnalysesPerAddress.GetValueOrDefault(address) >= MaxConcurrentAnalysesPerAddress)
            return StatusCode(429, new { error = "You already have comparisons running. Please wait for them to finish." });
        if (!joinsRunning)
            AnalysesPerAddress.AddOrUpdate(address, 1, (_, count) => count + 1);

        try
        {
            return await AnalyzeAsync(request, cancellationToken);
        }
        finally
        {
            if (!joinsRunning)
                ReleaseAnalysisSlot(address);
        }
    }

    private static void ReleaseAnalysisSlot(string address)
    {
        while (AnalysesPerAddress.TryGetValue(address, out var count))
        {
            var updated = count > 1
                ? AnalysesPerAddress.TryUpdate(address, count - 1, count)
                : ((ICollection<KeyValuePair<string, int>>)AnalysesPerAddress).Remove(new KeyValuePair<string, int>(address, count));
            if (updated)
                return;
        }
    }

    private async Task<ActionResult<AnalysisResult>> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
    {
        // Progress is pushed only to the connection that proved it owns the connection id
        if (!ProgressTokens.IsValid(_dataProtection, request.ConnectionId, request.ProgressToken))
            request.ConnectionId = null;

        try
        {
            var ipAddress = HttpContext.GetClientIpAddress();

            // Aborting the request stops the work unless another request waits on the same analysis
            using var work = WorkCancellation.Use(cancellationToken);
            var result = await _analysisService.AnalyzePackageAsync(request, ipAddress);
            await MarkViewedAsync(request.Registry, request.PackageId);
            result.Trust = await TrustOfAsync(request.Registry, request.PackageId);
            return Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new StatusCodeResult(ClientClosedRequest);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request for package analysis");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing package {PackageId}", request.PackageId);
            return StatusCode(500, new { error = "An error occurred while analyzing the package" });
        }
    }

    [HttpGet("{registry}/suggest")]
    [ValidatePackageRoute]
    public async Task<ActionResult<List<PackageSuggestion>>> GetSuggestions(
        string registry, [FromQuery] string q, [FromQuery] int limit = 5)
    {
        limit = Math.Min(limit, 50);
        try
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                return Ok(new List<PackageSuggestion>());

            var suggestions = await _analysisService.SearchSuggestionsAsync(registry, q, limit);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching suggestions for {Query}", q);
            return Ok(new List<PackageSuggestion>());
        }
    }

    // Recorded at most hourly per package and process, so a busy package costs one write an hour
    private async Task MarkViewedAsync(string registry, string packageId)
    {
        var key = $"viewed:{registry}:{packageId}".ToLowerInvariant();
        if (_memory.TryGetValue(key, out _))
            return;
        _memory.Set(key, true, TimeSpan.FromHours(1));
        try
        {
            await _cache.MarkViewedAsync(registry, packageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record a view of {PackageId}", packageId);
        }
    }

    private async Task<bool> MayQueueScrapeAsync()
        => await _scrapeBudget.TryReserveAsync(HttpContext.GetClientIpAddress());

    [HttpGet("{registry}/{packageId}")]
    [ValidatePackageRoute]
    public async Task<ActionResult<PackageMetadata>> GetPackageMetadata(string registry, string packageId)
    {
        try
        {
            var metadata = WithTrust(await _analysisService.GetPackageMetadataAsync(registry, packageId), await TrustOfAsync(registry, packageId));
            await MarkViewedAsync(registry, packageId);

            var hasUnscrapedVersions = metadata.Versions.Any(v => string.IsNullOrEmpty(v.ReleaseNotesSource));
            var cacheKey = $"scrape-attempt-{registry}-{packageId}";
            
            if (hasUnscrapedVersions && metadata.Versions.Count > 0 && !_workTracker.IsRunning(WorkKeys.Scrape(registry, packageId)))
            {
                if (await _distributedCache.GetAsync(cacheKey) == null && await MayQueueScrapeAsync())
                {
                    await _distributedCache.SetAsync(cacheKey, new byte[] { 1 },
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
                        });
                    _jobs.Enqueue<IPackageAnalysisService>(
                        service => service.ScrapePackageReleaseNotesAsync(packageId, registry));
                }
            }

            return Ok(metadata);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata for {PackageId}", packageId);
            return StatusCode(500, new { error = "An error occurred while fetching package metadata" });
        }
    }

    [HttpGet("registries")]
    public async Task<ActionResult<List<RegistryInfo>>> GetRegistries()
    {
        try
        {
            var registries = await _analysisService.GetRegistriesAsync();
            return Ok(registries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching registries");
            return StatusCode(500, new { error = "An error occurred while fetching registries" });
        }
    }

    [HttpGet("live-updates")]
    public async Task<ActionResult<PaginatedResult<LiveUpdate>>> GetLiveUpdates(int page = 1, int pageSize = 20)
    {
        try
        {
            var updates = await _liveUpdates.GetLiveUpdatesAsync(page, pageSize);
            return Ok(updates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching live updates");
            return StatusCode(500, new { error = "An error occurred while fetching live updates" });
        }
    }

    [HttpGet("cached")]
    public async Task<ActionResult<List<PackageMetadata>>> GetCachedPackages()
    {
        try
        {
            var cachedPackages = await _memory.GetOrCreateAsync("cached-packages", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return _analysisService.GetCachedPackagesAsync();
            });
            return Ok(cachedPackages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cached packages");
            return StatusCode(500, new { error = "An error occurred while fetching cached packages" });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet("{registry}/{packageId}/export/pdf")]
    [ValidatePackageRoute]
    public async Task<IActionResult> ExportReleaseNotesToPdf(string registry, string packageId)
    {
        try
        {
            var metadata = WithTrust(await _analysisService.GetPackageMetadataAsync(registry, packageId), await TrustOfAsync(registry, packageId));

            var fileContent = await _exportService.ExportToPdfAsync(metadata);
            var fileName = $"{packageId}-release-notes.pdf";
            
            return File(fileContent, "application/pdf", fileName);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting release notes for {PackageId}", packageId);
            return StatusCode(500, new { error = "An error occurred while exporting release notes" });
        }
    }

    [HttpPost("export/diff/pdf")]
    [ValidatePackageRoute]
    public async Task<IActionResult> ExportDiffToPdf([FromBody] AnalyzeRequest request, CancellationToken cancellationToken)
    {
        // The export runs a full analysis, so it takes one of the same slots
        if (RejectWhenBusy(request) is { } busy)
            return busy;

        if (!ProgressTokens.IsValid(_dataProtection, request.ConnectionId, request.ProgressToken))
            request.ConnectionId = null;

        try
        {
            var ipAddress = HttpContext.GetClientIpAddress();
            using var work = WorkCancellation.Use(cancellationToken);
            var diff = await _analysisService.AnalyzePackageAsync(request, ipAddress);
            diff.Trust = await TrustOfAsync(request.Registry, request.PackageId);

            var fileContent = await _exportService.ExportDiffToPdfAsync(diff);
            var fileName = $"{request.PackageId}-diff-{request.CurrentVersion}-to-{request.TargetVersion}.pdf";
            
            return File(fileContent, "application/pdf", fileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new StatusCodeResult(ClientClosedRequest);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request for diff export");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting diff for {PackageId}", request.PackageId);
            return StatusCode(500, new { error = "An error occurred while exporting diff" });
        }
    }

    // Security data is an addition to the page: when it cannot be read the page is served without it
    private async Task<PackageTrust?> TrustOfAsync(string registry, string packageId)
    {
        try
        {
            return await _trust.GetTrustAsync(registry, packageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Security data of {PackageId} could not be read", packageId);
            return null;
        }
    }

    // The metadata may be a cached object other requests hold too; the trust goes on a copy
    private static PackageMetadata WithTrust(PackageMetadata metadata, PackageTrust? trust) => new()
    {
        PackageId = metadata.PackageId,
        Registry = metadata.Registry,
        LatestVersion = metadata.LatestVersion,
        Description = metadata.Description,
        Authors = metadata.Authors,
        IconUrl = metadata.IconUrl,
        ProjectUrl = metadata.ProjectUrl,
        GitHubRepositoryUrl = metadata.GitHubRepositoryUrl,
        Versions = metadata.Versions,
        Trust = trust
    };
}
