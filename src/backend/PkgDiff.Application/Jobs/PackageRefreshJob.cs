using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Jobs;

// Refreshes one cached package for the recurring dispatcher (PackageAnalysisJob).
// A package that cannot be compared, because its registry lists a single version or no longer
// knows it, was cached when someone opened its page. That is not a failure of the refresh: every
// later run would fail the same way and fill the failed-job list of the dashboard.
public class PackageRefreshJob
{
    private readonly IPackageAnalysisService _analysisService;
    private readonly ILogger<PackageRefreshJob> _logger;

    public PackageRefreshJob(IPackageAnalysisService analysisService, ILogger<PackageRefreshJob> logger)
    {
        _analysisService = analysisService;
        _logger = logger;
    }

    public async Task RefreshAsync(AnalyzeRequest request)
    {
        try
        {
            await _analysisService.AnalyzePackageAsync(request, "System");
        }
        catch (ArgumentException ex)
        {
            _logger.LogInformation("Refresh of {Registry} {PackageId} skipped: {Reason}", request.Registry, request.PackageId, ex.Message);
        }
    }
}
