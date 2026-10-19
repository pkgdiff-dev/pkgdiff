using PkgDiff.Domain.Entities;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface ILiveUpdateService
{
    Task<PaginatedResult<LiveUpdate>> GetLiveUpdatesAsync(int page = 1, int pageSize = 20);
    Task<bool> RecordLatestVersionAsync(
        CachedPackage package,
        string registry,
        string latestVersion,
        DateTime? publishedDate,
        bool authoritative = false);
}
