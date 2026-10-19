using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminSecurityService
{
    Task<PaginatedResult<RequestLogInfo>> GetRequestLogsAsync(int page = 1, int pageSize = 50, string? ipFilter = null);
    Task<PaginatedResult<BlockedIpInfo>> GetBlockedIpsAsync(int page = 1, int pageSize = 20);
    Task<BlockedIpInfo> BlockIpAsync(BlockIpRequest request, string createdBy);
    Task UnblockIpAsync(int id);
    Task<List<IpStatistics>> GetIpStatisticsAsync(int topCount = 20);
    Task<bool> IsIpBlockedAsync(string ipAddress);
}
