using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Outbound;

public class RateLimiterServiceTests
{
    private static RateLimiterService Create(Dictionary<string, string?> settings) =>
        new(NullLogger<RateLimiterService>.Instance, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public async Task A_key_that_has_to_wait_does_not_hold_up_another_key()
    {
        var limiter = Create(new()
        {
            ["RateLimitSettings:GitHubScrapingMaxRequests"] = "1",
            ["RateLimitSettings:GitHubScrapingWindowSeconds"] = "3"
        });

        await limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));
        var waiting = limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));

        var stopwatch = Stopwatch.StartNew();
        await limiter.ExecuteAsync("npm-api", () => Task.FromResult(0));
        await limiter.ExecuteAsync("nuget-api", () => Task.FromResult(0));
        stopwatch.Stop();

        Assert.False(waiting.IsCompleted);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"other keys took {stopwatch.Elapsed}");

        await waiting;
    }

    [Fact]
    public async Task A_full_window_delays_the_next_request_of_the_same_key()
    {
        var limiter = Create(new()
        {
            ["RateLimitSettings:GitHubScrapingMaxRequests"] = "2",
            ["RateLimitSettings:GitHubScrapingWindowSeconds"] = "1"
        });

        await limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));
        Assert.True(limiter.CanExecute("github-scraping"));
        await limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));

        Assert.False(limiter.CanExecute("github-scraping"));
        Assert.InRange(limiter.GetTimeUntilNextSlot("github-scraping"), TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1));

        var stopwatch = Stopwatch.StartNew();
        await limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));

        Assert.True(stopwatch.Elapsed > TimeSpan.FromMilliseconds(700), $"waited only {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Concurrent_use_of_one_bucket_neither_fails_nor_exceeds_the_limit()
    {
        var limiter = Create(new()
        {
            ["RateLimitSettings:NpmApiMaxRequests"] = "100000",
            ["RateLimitSettings:NpmApiWindowSeconds"] = "60"
        });
        var executed = 0;

        var work = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                await limiter.ExecuteAsync("npm-api", () => Task.FromResult(Interlocked.Increment(ref executed)));
                limiter.CanExecute("npm-api");
                limiter.GetTimeUntilNextSlot("npm-api");
            }
        }));
        await Task.WhenAll(work);

        Assert.Equal(64 * 200, executed);
    }
}
