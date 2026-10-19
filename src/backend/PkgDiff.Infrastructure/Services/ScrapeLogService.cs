using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.Infrastructure.Services;

public class ScrapeLogService : IScrapeLogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ScrapeLogService> _logger;

    public ScrapeLogService(IUnitOfWork unitOfWork, ILogger<ScrapeLogService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task LogFailureAsync(
        string packageId,
        string version,
        string registry,
        string? githubRepoUrl,
        string failureReason,
        string? availableVersions,
        string attemptedSources)
    {
        try
        {
            // Avoid duplicate logs for the same package+version (ignoring reason)
            var existing = await _unitOfWork.ScrapeLogs.FindAsync(
                l => l.PackageId == packageId
                     && l.Version == version
                     && !l.IsResolved);

            if (existing.Any())
                return;

            var log = new ScrapeLog
            {
                PackageId = packageId,
                Version = version,
                Registry = registry,
                GitHubRepoUrl = githubRepoUrl,
                FailureReason = failureReason,
                AvailableVersions = availableVersions?.Length > 2000
                    ? availableVersions[..2000]
                    : availableVersions,
                AttemptedSources = attemptedSources,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.ScrapeLogs.AddAsync(log);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never let logging failures break the main flow
            _logger.LogError(ex, "[ScrapeLogService] Failed to persist scrape log for {PackageId} v{Version}", packageId, version);
        }
    }
}
