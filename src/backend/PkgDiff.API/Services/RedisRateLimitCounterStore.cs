using StackExchange.Redis;

namespace PkgDiff.API.Services;

public class RedisRateLimitCounterStore : IRateLimitCounterStore
{
    // One script, so a counter can never be left without an expiry: INCR followed by a separate
    // EXPIRE would block an address for good if the process stopped between the two commands
    private const string IncrementScript = @"
local count = redis.call('INCR', KEYS[1])
if count == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return count";

    private readonly IConnectionMultiplexer _redis;

    public RedisRateLimitCounterStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<int> IncrementAsync(string key, TimeSpan ttl)
    {
        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            IncrementScript,
            new RedisKey[] { key },
            new RedisValue[] { (long)ttl.TotalMilliseconds });
        return (int)result;
    }
}
