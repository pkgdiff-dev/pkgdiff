namespace PkgDiff.API.Services;

public interface IRateLimitCounterStore
{
    /// <summary>
    /// Atomically increments the counter for the given key and returns the new value.
    /// Sets the TTL on first increment.
    /// </summary>
    Task<int> IncrementAsync(string key, TimeSpan ttl);
}
