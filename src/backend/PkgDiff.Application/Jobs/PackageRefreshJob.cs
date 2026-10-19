using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Jobs;

// A package that cannot be compared is skipped, not failed: every later run would fail the same way.
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
