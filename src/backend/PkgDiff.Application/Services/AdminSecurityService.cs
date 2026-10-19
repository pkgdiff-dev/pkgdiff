using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminSecurityService : IAdminSecurityService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminSecurityService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PaginatedResult<RequestLogInfo>> GetRequestLogsAsync(int page = 1, int pageSize = 50, string? ipFilter = null)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var term = ipFilter?.Trim().ToLowerInvariant() ?? string.Empty;

        var (logs, total) = await _unitOfWork.RequestLogs.GetPageDescendingAsync(
            x => term == "" || x.IpAddress.ToLower().Contains(term),
            x => x.CreatedAt,
            x => new RequestLogInfo
            {
                Id = x.Id,
                IpAddress = x.IpAddress,
                Endpoint = x.Endpoint,
                Method = x.Method,
                StatusCode = x.StatusCode,
                CreatedAt = x.CreatedAt,
                UserAgent = x.UserAgent
            },
            page,
            pageSize);

        return Paging.Result(logs, total, page, pageSize);
    }

    public async Task<PaginatedResult<BlockedIpInfo>> GetBlockedIpsAsync(int page = 1, int pageSize = 20)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var now = DateTime.UtcNow;

        // Expired temporary blocks are neither listed nor counted
        var (blocks, total) = await _unitOfWork.BlockedIps.GetPageDescendingAsync(
            x => x.BlockType == BlockType.Permanent || (x.ExpiresAt != null && x.ExpiresAt > now),
            x => x.CreatedAt,
            x => new BlockedIpInfo
            {
                Id = x.Id,
                IpAddress = x.IpAddress,
                Reason = x.Reason,
                BlockType = x.BlockType.ToString(),
                ExpiresAt = x.ExpiresAt,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy
            },
            page,
            pageSize);

        return Paging.Result(blocks, total, page, pageSize);
    }

    public async Task<BlockedIpInfo> BlockIpAsync(BlockIpRequest request, string createdBy)
    {
        var blockType = request.BlockType.Equals("Permanent", StringComparison.OrdinalIgnoreCase)
            ? BlockType.Permanent
            : BlockType.Temporary;

        DateTime? expiresAt = null;
        if (blockType == BlockType.Temporary && request.DurationMinutes.HasValue)
        {
            expiresAt = DateTime.UtcNow.AddMinutes(request.DurationMinutes.Value);
        }

        var entity = new BlockedIp
        {
            IpAddress = request.IpAddress,
            Reason = request.Reason,
            BlockType = blockType,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        var created = await _unitOfWork.BlockedIps.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return new BlockedIpInfo
        {
            Id = created.Id,
            IpAddress = created.IpAddress,
            Reason = created.Reason,
            BlockType = created.BlockType.ToString(),
            ExpiresAt = created.ExpiresAt,
            CreatedAt = created.CreatedAt,
            CreatedBy = created.CreatedBy
        };
    }

    public async Task UnblockIpAsync(int id)
    {
        var entity = await _unitOfWork.BlockedIps.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Blocked IP with id {id} not found");

        await _unitOfWork.BlockedIps.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<List<IpStatistics>> GetIpStatisticsAsync(int topCount = 20)
    {
        topCount = Math.Clamp(topCount, 1, Paging.MaxPageSize);
        var logs = _unitOfWork.RequestLogs.Query();

        var top = await _unitOfWork.ToListAsync(logs
            .GroupBy(x => x.IpAddress)
            .Select(g => new
            {
                IpAddress = g.Key,
                RequestCount = g.Count(),
                FirstSeen = g.Min(x => x.CreatedAt),
                LastSeen = g.Max(x => x.CreatedAt)
            })
            .OrderByDescending(x => x.RequestCount)
            .ThenBy(x => x.IpAddress)
            .Take(topCount));

        var addresses = top.Select(x => x.IpAddress).ToList();

        var endpointCounts = await _unitOfWork.ToListAsync(logs
            .Where(x => addresses.Contains(x.IpAddress))
            .GroupBy(x => new { x.IpAddress, x.Endpoint })
            .Select(g => new { g.Key.IpAddress, g.Key.Endpoint, Count = g.Count() }));
        var topEndpoints = endpointCounts
            .GroupBy(x => x.IpAddress)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Count).ThenBy(x => x.Endpoint).Take(3).Select(x => x.Endpoint).ToList());

        var blocked = (await _unitOfWork.ToListAsync(_unitOfWork.BlockedIps.Query()
                .Where(x => addresses.Contains(x.IpAddress))
                .Select(x => x.IpAddress)))
            .ToHashSet();

        return top
            .Select(x => new IpStatistics
            {
                IpAddress = x.IpAddress,
                RequestCount = x.RequestCount,
                FirstSeen = x.FirstSeen,
                LastSeen = x.LastSeen,
                TopEndpoints = topEndpoints.GetValueOrDefault(x.IpAddress, new List<string>()),
                IsBlocked = blocked.Contains(x.IpAddress)
            })
            .ToList();
    }

    public async Task<bool> IsIpBlockedAsync(string ipAddress)
    {
        var blocked = await _unitOfWork.BlockedIps.FindAsync(x => x.IpAddress == ipAddress);
        var activeBlock = blocked.FirstOrDefault(x =>
            x.BlockType == BlockType.Permanent ||
            (x.ExpiresAt.HasValue && x.ExpiresAt.Value > DateTime.UtcNow));

        return activeBlock != null;
    }
}
