namespace PkgDiff.Application.Interfaces;

/// <summary>
/// Service for tracking and managing GitHub API rate limits based on response headers
/// </summary>
public interface IGitHubRateLimitHandler
{
    /// <summary>
    /// Counts one request against the budget, or throws <see cref="GitHubRateLimitedException"/>
    /// when the budget is spent until the rate limit resets
    /// </summary>
    /// <returns>True if request can proceed</returns>
    Task<bool> CanMakeRequestAsync();

    /// <summary>
    /// Updates rate limit state from GitHub API response headers
    /// </summary>
    /// <param name="response">HTTP response containing rate limit headers</param>
    Task UpdateFromHeadersAsync(HttpResponseMessage response);

    /// <summary>
    /// Gets the current rate limit status
    /// </summary>
    /// <returns>Tuple of (remaining, limit, reset time)</returns>
    Task<(int Remaining, int Limit, DateTime? ResetTime)> GetRateLimitStatusAsync();
}
