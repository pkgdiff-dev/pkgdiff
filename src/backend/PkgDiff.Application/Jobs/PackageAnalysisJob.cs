using Microsoft.Extensions.Logging;
using PkgDiff.Shared.DTOs;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.Application.Jobs
{
    public class PackageAnalysisJob
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<PackageAnalysisJob> _logger;
        private readonly IJobScheduler _jobs;

        public PackageAnalysisJob(
            IUnitOfWork unitOfWork,
            ILogger<PackageAnalysisJob> logger,
            IJobScheduler jobs)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _jobs = jobs;
        }

        private static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(30);

        public async Task ExecuteAsync()
        {
            _logger.LogInformation("Starting package analysis dispatcher...");

            try
            {
                // Only packages viewed in the last 30 days; an unvisited one refreshes when it is opened again
                var cutoff = DateTime.UtcNow - RefreshWindow;
                var packages = await _unitOfWork.ToListAsync(_unitOfWork.CachedPackages.Query()
                    .Where(p => (p.LastViewedAt ?? p.CachedAt) > cutoff)
                    .Select(p => new { p.PackageId, Registry = p.Registry.Value }));

                _logger.LogInformation("Found {Count} packages to refresh. Enqueuing jobs...", packages.Count);

                foreach (var package in packages)
                {
                    var request = new AnalyzeRequest
                    {
                        PackageId = package.PackageId,
                        Registry = package.Registry,
                        CurrentVersion = string.Empty,
                        TargetVersion = string.Empty,
                        IncludePrerelease = false
                    };

                    // One job per package; one that cannot be compared is skipped there instead of failing
                    _jobs.Enqueue<PackageRefreshJob>(x => x.RefreshAsync(request));
                }

                _logger.LogInformation("All package analysis jobs have been enqueued.");
            }
            catch (Exception ex)
            {
                // Rethrown so Hangfire marks the run as failed and retries it instead of reporting success
                _logger.LogError(ex, "Error occurred during package analysis dispatch");
                throw;
            }
        }
    }
}
