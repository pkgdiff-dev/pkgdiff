using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminScrapeLogService : IAdminScrapeLogService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminScrapeLogService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PaginatedResult<ScrapeLogInfo>> GetScrapeLogsAsync(int page = 1, int pageSize = 20, string? packageFilter = null)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var term = packageFilter?.Trim().ToLowerInvariant() ?? string.Empty;

        var (logs, total) = await _unitOfWork.ScrapeLogs.GetPageDescendingAsync(
            x => term == "" || x.PackageId.ToLower().Contains(term),
            x => x.CreatedAt,
            x => new ScrapeLogInfo
            {
                Id = x.Id,
                PackageId = x.PackageId,
                Version = x.Version,
                Registry = x.Registry,
                GitHubRepoUrl = x.GitHubRepoUrl,
                FailureReason = x.FailureReason,
                AvailableVersions = x.AvailableVersions,
                AttemptedSources = x.AttemptedSources,
                IsResolved = x.IsResolved,
                CreatedAt = x.CreatedAt
            },
            page,
            pageSize);

        return Paging.Result(logs, total, page, pageSize);
    }

    public async Task<ScrapeLogSummary> GetScrapeLogSummaryAsync()
    {
        var unresolved = _unitOfWork.ScrapeLogs.Query().Where(x => !x.IsResolved);

        var topFailing = await _unitOfWork.ToListAsync(unresolved
            .GroupBy(x => x.PackageId)
            .Select(g => new { PackageId = g.Key, FailureCount = g.Count(), LastOccurrence = g.Max(x => x.CreatedAt) })
            .OrderByDescending(x => x.FailureCount)
            .ThenBy(x => x.PackageId)
            .Take(20));

        // The reason of the latest failure of each of those packages, in one query
        var packageIds = topFailing.Select(x => x.PackageId).ToList();
        var lastOccurrences = topFailing.Select(x => x.LastOccurrence).ToList();
        var latestReasons = (await _unitOfWork.ToListAsync(unresolved
                .Where(x => packageIds.Contains(x.PackageId) && lastOccurrences.Contains(x.CreatedAt))
                .Select(x => new { x.PackageId, x.CreatedAt, x.FailureReason, x.Id })))
            .GroupBy(x => x.PackageId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First().FailureReason);

        return new ScrapeLogSummary
        {
            TotalFailures = await _unitOfWork.ScrapeLogs.CountAsync(),
            UnresolvedCount = await _unitOfWork.CountAsync(unresolved),
            TopFailingPackages = topFailing
                .Select(x => new ScrapeLogPackageSummary
                {
                    PackageId = x.PackageId,
                    FailureCount = x.FailureCount,
                    LatestReason = latestReasons.GetValueOrDefault(x.PackageId, string.Empty),
                    LastOccurrence = x.LastOccurrence
                })
                .ToList()
        };
    }

    public async Task ResolveScrapeLogAsync(int id)
    {
        var entity = await _unitOfWork.ScrapeLogs.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Scrape log with id {id} not found");

        entity.IsResolved = true;
        await _unitOfWork.ScrapeLogs.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ResolveAllScrapeLogsForPackageAsync(string packageId)
    {
        var logs = await _unitOfWork.ScrapeLogs.FindAsync(
            x => x.PackageId == packageId && !x.IsResolved);

        foreach (var log in logs)
        {
            log.IsResolved = true;
            await _unitOfWork.ScrapeLogs.UpdateAsync(log);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    public async Task DeleteScrapeLogAsync(int id)
    {
        var entity = await _unitOfWork.ScrapeLogs.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Scrape log with id {id} not found");
        await _unitOfWork.ScrapeLogs.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ClearResolvedScrapeLogsAsync()
    {
        await _unitOfWork.ScrapeLogs.DeleteWhereAsync(x => x.IsResolved);
    }
}
