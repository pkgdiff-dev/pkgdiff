using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using PkgDiff.Infrastructure.Data;
using System.Net;
using System.Text.Json;
using PkgDiff.API.Extensions;

namespace PkgDiff.API.Middleware;

public class IpBlockingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
    };

    public IpBlockingMiddleware(RequestDelegate next, IDistributedCache cache)
    {
        _next = next;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        var ipAddress = context.GetClientIpAddress();
        var cacheKey = $"blocked_ip_{ipAddress}";

        var cachedBytes = await _cache.GetAsync(cacheKey);
        Domain.Entities.BlockedIp? blockedIp;

        if (cachedBytes == null)
        {
            blockedIp = await dbContext.BlockedIps
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.IpAddress == ipAddress);

            // Cache both outcomes: null = "not blocked", object = "blocked"
            await _cache.SetAsync(
                cacheKey,
                JsonSerializer.SerializeToUtf8Bytes(blockedIp),
                CacheOptions);
        }
        else
        {
            blockedIp = JsonSerializer.Deserialize<Domain.Entities.BlockedIp>(cachedBytes);
        }

        if (blockedIp != null)
        {
            if (blockedIp.BlockType == Domain.Entities.BlockType.Temporary
                && blockedIp.ExpiresAt.HasValue
                && blockedIp.ExpiresAt.Value < DateTime.UtcNow)
            {
                dbContext.BlockedIps.Remove(blockedIp);
                await dbContext.SaveChangesAsync();
                await _cache.RemoveAsync(cacheKey);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Forbidden",
                    message = "Your IP address has been blocked."
                });
                return;
            }
        }

        await _next(context);
    }
}
