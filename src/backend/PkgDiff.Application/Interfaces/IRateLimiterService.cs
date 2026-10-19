namespace PkgDiff.Application.Interfaces;

/// <summary>
/// Service for managing rate limits for external API calls (e.g., NuGet.org web scraping)
/// </summary>
public interface IRateLimiterService
{
    /// <summary>
    /// Waits if necessary to respect rate limits, then executes the action
    /// </summary>
    /// <param name="key">Rate limit key (e.g., "nuget-scraping")</param>
    /// <param name="action">Action to execute</param>
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action);

    /// <summary>
    /// Checks if the rate limit allows execution without waiting
    /// </summary>
    bool CanExecute(string key);

    /// <summary>
    /// Gets the time until the next available slot
    /// </summary>
    TimeSpan GetTimeUntilNextSlot(string key);
}
