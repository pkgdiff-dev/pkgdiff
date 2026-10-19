using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using System.Collections.Concurrent;

namespace PkgDiff.Infrastructure.Services;

public class RateLimiterService : IRateLimiterService
{
    private const string GitHubApiKey = "github-api";
    
    private readonly ILogger<RateLimiterService> _logger;
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private readonly Dictionary<string, RateLimitConfig> _configs;
    private readonly SemaphoreSlim _githubApiSemaphore;

    public RateLimiterService(ILogger<RateLimiterService> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        var rateLimitSettings = configuration.GetSection("RateLimitSettings");
        
        var maxConcurrentGitHubRequests = rateLimitSettings.GetValue<int>("GitHubApiMaxConcurrent", 50);
        _githubApiSemaphore = new SemaphoreSlim(maxConcurrentGitHubRequests, maxConcurrentGitHubRequests);
        
        _configs = new()
        {
            // nuget.org allows 50 requests per minute; the default stays below it
            ["nuget-scraping"] = new RateLimitConfig 
            { 
                MaxRequests = rateLimitSettings.GetValue<int>("NuGetScrapingMaxRequests", 45),
                WindowSeconds = rateLimitSettings.GetValue<int>("NuGetScrapingWindowSeconds", 60)
            },
            
            ["nuget-api"] = new RateLimitConfig 
            { 
                MaxRequests = rateLimitSettings.GetValue<int>("NuGetApiMaxRequests", 1000),
                WindowSeconds = rateLimitSettings.GetValue<int>("NuGetApiWindowSeconds", 60)
            },
            
            ["npm-api"] = new RateLimitConfig 
            { 
                MaxRequests = rateLimitSettings.GetValue<int>("NpmApiMaxRequests", 500),
                WindowSeconds = rateLimitSettings.GetValue<int>("NpmApiWindowSeconds", 60)
            },
            
            ["github-scraping"] = new RateLimitConfig 
            { 
                MaxRequests = rateLimitSettings.GetValue<int>("GitHubScrapingMaxRequests", 30),
                WindowSeconds = rateLimitSettings.GetValue<int>("GitHubScrapingWindowSeconds", 60)
            },
            
            [GitHubApiKey] = new RateLimitConfig 
            { 
                MaxRequests = rateLimitSettings.GetValue<int>("GitHubApiMaxRequests", 5000),
                WindowSeconds = rateLimitSettings.GetValue<int>("GitHubApiWindowSeconds", 3600)
            }
        };
    }

    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action)
    {
        var bucket = _buckets.GetOrAdd(key, k => new RateLimitBucket());
        var config = _configs.GetValueOrDefault(key, new RateLimitConfig { MaxRequests = 100, WindowSeconds = 60 });

        if (key == GitHubApiKey)
        {
            await _githubApiSemaphore.WaitAsync();
            try
            {
                return await ExecuteWithRateLimitAsync(key, bucket, config, action);
            }
            finally
            {
                _githubApiSemaphore.Release();
            }
        }

        return await ExecuteWithRateLimitAsync(key, bucket, config, action);
    }

    private async Task<T> ExecuteWithRateLimitAsync<T>(string key, RateLimitBucket bucket, RateLimitConfig config, Func<Task<T>> action)
    {
        // One gate per key: a wait for GitHub must not hold up NuGet, npm, PyPI or pub.dev
        await bucket.Gate.WaitAsync(WorkCancellation.Token);
        try
        {
            var waitTime = bucket.TimeUntilNextSlot(config);
            if (waitTime > TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "[RateLimiter] Rate limit reached for '{Key}'. Waiting {WaitSeconds:F2}s. ({Max} requests in {Window}s window)",
                    key, waitTime.TotalSeconds, config.MaxRequests, config.WindowSeconds);

                // The gate stays held during the wait so callers of this key leave one by one
                await Task.Delay(waitTime, WorkCancellation.Token);
            }

            bucket.Record(config);
        }
        finally
        {
            bucket.Gate.Release();
        }

        return await action();
    }

    public bool CanExecute(string key)
    {
        return GetTimeUntilNextSlot(key) <= TimeSpan.Zero;
    }

    public TimeSpan GetTimeUntilNextSlot(string key)
    {
        var bucket = _buckets.GetOrAdd(key, k => new RateLimitBucket());
        var config = _configs.GetValueOrDefault(key, new RateLimitConfig { MaxRequests = 100, WindowSeconds = 60 });

        return bucket.TimeUntilNextSlot(config);
    }

    private class RateLimitBucket
    {
        private readonly Queue<DateTime> _timestamps = new();

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public TimeSpan TimeUntilNextSlot(RateLimitConfig config)
        {
            lock (_timestamps)
            {
                var now = DateTime.UtcNow;
                Prune(now, config);
                if (_timestamps.Count < config.MaxRequests)
                    return TimeSpan.Zero;

                var wait = _timestamps.Peek().AddSeconds(config.WindowSeconds) - now;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
        }

        public void Record(RateLimitConfig config)
        {
            lock (_timestamps)
            {
                var now = DateTime.UtcNow;
                Prune(now, config);
                _timestamps.Enqueue(now);
            }
        }

        // Timestamps are enqueued in time order, so the oldest is always at the head
        private void Prune(DateTime now, RateLimitConfig config)
        {
            var cutoff = now.AddSeconds(-config.WindowSeconds);
            while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                _timestamps.Dequeue();
        }
    }

    private class RateLimitConfig
    {
        public int MaxRequests { get; set; }
        public int WindowSeconds { get; set; }
    }
}
