using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PkgDiff.Infrastructure.Data;
using StackExchange.Redis;

namespace PkgDiff.API.Services;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _context;

    public DatabaseHealthCheck(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await _context.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database is not reachable.");
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public RedisHealthCheck(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // With CacheSettings:Backend=Memory there is no Redis to wait for
        var redis = _services.GetService<IConnectionMultiplexer>();
        if (redis == null)
            return HealthCheckResult.Healthy("Redis is not configured.");

        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", ex);
        }
    }
}

public static class HealthResponses
{
    public const string ReadyTag = "ready";

    // Names and states only: a health endpoint is unauthenticated, so it never carries exception text
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString().ToLowerInvariant())
        }));
    }
}
