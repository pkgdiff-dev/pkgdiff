using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Services;

public static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Clamp(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    public static PaginatedResult<T> Result<T>(List<T> items, int total, int page, int pageSize) => new()
    {
        Data = items,
        Page = page,
        PageSize = pageSize,
        Total = total,
        TotalPages = (int)Math.Ceiling(total / (double)pageSize)
    };
}
