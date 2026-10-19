using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminDashboardService
{
    Task<DashboardStats> GetDashboardStatsAsync();

    Task<PaginatedResult<RecentSearch>> GetSearchHistoryAsync(int page = 1, int pageSize = 20);
    Task DeleteSearchHistoryAsync(int id);
    Task ClearSearchHistoryAsync();
}
