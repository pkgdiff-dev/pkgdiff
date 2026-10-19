using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminScrapeLogService
{
    Task<PaginatedResult<ScrapeLogInfo>> GetScrapeLogsAsync(int page = 1, int pageSize = 20, string? packageFilter = null);
    Task<ScrapeLogSummary> GetScrapeLogSummaryAsync();
    Task ResolveScrapeLogAsync(int id);
    Task ResolveAllScrapeLogsForPackageAsync(string packageId);
    Task DeleteScrapeLogAsync(int id);
    Task ClearResolvedScrapeLogsAsync();
}
