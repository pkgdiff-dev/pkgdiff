using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminDashboardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private const string SystemCaller = "System";

    // ───── Dashboard ─────

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        // Background refreshes record "System" as their caller; they are not searches
        var searches = _unitOfWork.SearchHistories.Query().Where(s => s.IpAddress != SystemCaller);

        var totalSearches = await _unitOfWork.CountAsync(searches);
        var distinctPackages = await _unitOfWork.CountAsync(searches.Select(s => s.PackageId).Distinct());
        var cachedPackages = await _unitOfWork.CachedPackages.CountAsync();

        var topPackages = (await _unitOfWork.ToListAsync(searches
                .GroupBy(s => s.PackageId)
                .Select(g => new { PackageId = g.Key, SearchCount = g.Count() })
                .OrderByDescending(p => p.SearchCount)
                .ThenBy(p => p.PackageId)
                .Take(10)))
            .Select(p => new PopularPackage { PackageId = p.PackageId, SearchCount = p.SearchCount })
            .ToList();

        var recentSearches = await _unitOfWork.ToListAsync(searches
            .OrderByDescending(s => s.SearchedAt)
            .ThenByDescending(s => s.Id)
            .Take(10)
            .Select(s => new RecentSearch
            {
                Id = s.Id,
                PackageId = s.PackageId,
                VersionRange = s.FromVersion + " → " + s.ToVersion,
                SearchedAt = s.SearchedAt,
                ProcessingTime = s.ProcessingTime,
                IpAddress = s.IpAddress
            }));

        var since = DateTime.UtcNow.AddDays(-7);
        var searchesByDay = (await _unitOfWork.ToListAsync(searches
                .Where(s => s.SearchedAt >= since)
                .GroupBy(s => s.SearchedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })))
            .ToDictionary(d => d.Day.ToString("yyyy-MM-dd"), d => d.Count);

        return new DashboardStats
        {
            TotalSearches = totalSearches,
            TotalPackagesAnalyzed = distinctPackages,
            CachedPackages = cachedPackages,
            TopPackages = topPackages,
            RecentSearches = recentSearches,
            SearchesByDay = searchesByDay
        };
    }

    // ───── Search History ─────

    public async Task<PaginatedResult<RecentSearch>> GetSearchHistoryAsync(int page = 1, int pageSize = 20)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var (searches, total) = await _unitOfWork.SearchHistories.GetPageDescendingAsync(
            s => s.IpAddress != SystemCaller,
            s => s.SearchedAt,
            s => new RecentSearch
            {
                Id = s.Id,
                PackageId = s.PackageId,
                VersionRange = s.FromVersion + " → " + s.ToVersion,
                SearchedAt = s.SearchedAt,
                ProcessingTime = s.ProcessingTime,
                IpAddress = s.IpAddress
            },
            page,
            pageSize);

        return Paging.Result(searches, total, page, pageSize);
    }

    public async Task DeleteSearchHistoryAsync(int id)
    {
        var entity = await _unitOfWork.SearchHistories.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Search history with id {id} not found");
        await _unitOfWork.SearchHistories.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ClearSearchHistoryAsync()
    {
        await _unitOfWork.SearchHistories.DeleteWhereAsync(_ => true);
    }
}
