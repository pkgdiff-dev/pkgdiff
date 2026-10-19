using PkgDiff.API.Services;

namespace PkgDiff.Tests.Edge;

public class MemoryRateLimitCounterStoreTests
{
    private readonly MemoryRateLimitCounterStore _store = new();

    [Fact]
    public async Task Concurrent_increments_of_one_key_are_all_counted()
    {
        var work = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
                await _store.IncrementAsync("key", TimeSpan.FromMinutes(1));
        }));
        await Task.WhenAll(work);

        Assert.Equal(32 * 500, _store.GetCount("key"));
    }

    [Fact]
    public async Task A_window_ends_when_its_time_is_up_even_under_steady_traffic()
    {
        var ttl = TimeSpan.FromMilliseconds(600);

        Assert.Equal(1, await _store.IncrementAsync("key", ttl));
        await Task.Delay(350, TestContext.Current.CancellationToken);
        Assert.Equal(2, await _store.IncrementAsync("key", ttl));
        await Task.Delay(350, TestContext.Current.CancellationToken);

        // 700 ms after the first request: a sliding expiry would still be counting, and return 3
        Assert.Equal(1, await _store.IncrementAsync("key", ttl));
    }

    [Fact]
    public async Task Keys_are_counted_separately_and_an_unknown_key_has_no_count()
    {
        await _store.IncrementAsync("a", TimeSpan.FromMinutes(1));
        await _store.IncrementAsync("a", TimeSpan.FromMinutes(1));
        await _store.IncrementAsync("b", TimeSpan.FromMinutes(1));

        Assert.Equal(2, _store.GetCount("a"));
        Assert.Equal(1, _store.GetCount("b"));
        Assert.Null(_store.GetCount("c"));
    }
}
