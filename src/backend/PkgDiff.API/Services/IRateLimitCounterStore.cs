namespace PkgDiff.API.Services;

public interface IRateLimitCounterStore
{
    // Atomic; the TTL is set on the first increment only
    Task<int> IncrementAsync(string key, TimeSpan ttl);
}
