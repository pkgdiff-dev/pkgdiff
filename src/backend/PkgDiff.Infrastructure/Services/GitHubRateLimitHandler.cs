using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

/// <summary>
/// Singleton service for tracking GitHub API rate limits from response headers
/// </summary>
public class GitHubRateLimitHandler : IGitHubRateLimitHandler
{
    private const int SafetyBufferThreshold = 10; // Minimum requests before waiting for reset
    private const int WarningThreshold = 100; // Threshold for logging warnings
    
    private readonly ILogger<GitHubRateLimitHandler> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private int _remaining = 5000; // Default for authenticated
    private int _limit = 5000;
    private DateTime? _resetTime;

    public GitHubRateLimitHandler(ILogger<GitHubRateLimitHandler> logger)
    {
        _logger = logger;
    }

    public async Task<bool> CanMakeRequestAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;

            // The window has passed: the budget is full again until the next answer says otherwise
            if (_resetTime.HasValue && _resetTime.Value <= now)
            {
                _remaining = _limit;
                _resetTime = null;
            }

            // Safety buffer: the caller stops instead of waiting, so background workers are not held
            // for up to an hour and its work can be scheduled for the reset
            if (_remaining <= SafetyBufferThreshold && _resetTime.HasValue)
            {
                _logger.LogDebug(
                    "[GitHubRateLimit] Rate limit nearly exhausted ({Remaining}/{Limit}) until {ResetTime}",
                    _remaining, _limit, _resetTime.Value);
                throw new GitHubRateLimitedException(_resetTime.Value);
            }

            // Counted before the answer arrives, so requests running in parallel cannot spend the buffer
            _remaining--;

            // Log warning when approaching limit
            if (_remaining < WarningThreshold && _remaining > SafetyBufferThreshold)
            {
                _logger.LogWarning(
                    "[GitHubRateLimit] Approaching rate limit: {Remaining}/{Limit} requests remaining (resets at {ResetTime})",
                    _remaining, _limit, _resetTime);
            }
            
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpdateFromHeadersAsync(HttpResponseMessage response)
    {
        await _semaphore.WaitAsync();
        try
        {
            // Parse x-ratelimit-remaining
            if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues))
            {
                var remainingStr = remainingValues.FirstOrDefault();
                if (int.TryParse(remainingStr, out var remaining))
                {
                    _remaining = remaining;
                }
            }

            // Parse x-ratelimit-limit
            if (response.Headers.TryGetValues("x-ratelimit-limit", out var limitValues))
            {
                var limitStr = limitValues.FirstOrDefault();
                if (int.TryParse(limitStr, out var limit))
                {
                    _limit = limit;
                }
            }

            // Parse x-ratelimit-reset (Unix timestamp)
            if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues))
            {
                var resetStr = resetValues.FirstOrDefault();
                if (long.TryParse(resetStr, out var resetUnix))
                {
                    _resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix).UtcDateTime;
                }
            }

            _logger.LogDebug(
                "[GitHubRateLimit] Updated from headers: {Remaining}/{Limit} remaining (resets at {ResetTime})",
                _remaining, _limit, _resetTime);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<(int Remaining, int Limit, DateTime? ResetTime)> GetRateLimitStatusAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return (_remaining, _limit, _resetTime);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
