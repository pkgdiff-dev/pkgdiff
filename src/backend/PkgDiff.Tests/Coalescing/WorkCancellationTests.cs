using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Coalescing;

public class WorkCancellationTests
{
    private readonly WorkTrackerService _tracker = new();

    // The shared work of these tests: it reports the token it runs under and ends only when told to or when cancelled
    private sealed class SharedWork
    {
        private readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CancellationToken> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Runs;

        public Task<CancellationToken> Started => _started.Task;

        public void Complete(string value) => _result.TrySetResult(value);

        public async Task<string> RunAsync()
        {
            Interlocked.Increment(ref Runs);
            var token = WorkCancellation.Token;
            _started.TrySetResult(token);
            return await _result.Task.WaitAsync(token);
        }
    }

    // What an endpoint does: the request's token is the ambient one while the call is made
    private Task<string> CallAsync(SharedWork work, CancellationToken requestToken)
    {
        using (WorkCancellation.Use(requestToken))
            return _tracker.RunCoalescedAsync("key", work.RunAsync);
    }

    [Fact]
    public async Task The_run_stops_when_its_only_caller_leaves()
    {
        var work = new SharedWork();
        using var request = new CancellationTokenSource();

        var call = CallAsync(work, request.Token);
        var workToken = await work.Started;
        request.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(workToken.IsCancellationRequested);
        Assert.NotEqual(request.Token, workToken);
        Assert.False(_tracker.IsRunning("key"));
    }

    [Fact]
    public async Task The_run_goes_on_while_another_caller_still_waits_for_it()
    {
        var work = new SharedWork();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var firstCall = CallAsync(work, first.Token);
        var secondCall = CallAsync(work, second.Token);
        var workToken = await work.Started;
        first.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstCall);
        Assert.False(workToken.IsCancellationRequested);
        Assert.True(_tracker.IsRunning("key"));

        work.Complete("done");
        Assert.Equal("done", await secondCall);
        Assert.Equal(1, work.Runs);
    }

    [Fact]
    public async Task A_caller_without_a_request_token_keeps_the_run_alive()
    {
        var work = new SharedWork();
        using var request = new CancellationTokenSource();

        var background = _tracker.RunCoalescedAsync("key", work.RunAsync);
        var visitor = CallAsync(work, request.Token);
        var workToken = await work.Started;
        request.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => visitor);

        Assert.False(workToken.IsCancellationRequested);
        work.Complete("done");
        Assert.Equal("done", await background);
    }

    [Fact]
    public async Task A_caller_that_arrives_after_everyone_left_gets_a_run_of_its_own()
    {
        var abandoned = new SharedWork();
        using var request = new CancellationTokenSource();
        var call = CallAsync(abandoned, request.Token);
        await abandoned.Started;
        request.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        var fresh = new SharedWork();
        var next = CallAsync(fresh, CancellationToken.None);
        fresh.Complete("fresh");

        Assert.Equal("fresh", await next);
        Assert.Equal(1, fresh.Runs);
    }

    [Fact]
    public async Task An_outbound_request_ends_when_its_work_is_cancelled()
    {
        using var client = new HttpClient(new WorkCancellationHandler { InnerHandler = new HangingHandler() });
        using var work = new CancellationTokenSource();

        Task<HttpResponseMessage> request;
        using (WorkCancellation.Use(work.Token))
            request = client.GetAsync("http://registry.test/package", TestContext.Current.CancellationToken);
        work.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task An_outbound_request_outside_any_work_is_passed_on_unchanged()
    {
        var inner = new HangingHandler { Answer = true };
        using var client = new HttpClient(new WorkCancellationHandler { InnerHandler = inner });

        using var response = await client.GetAsync("http://registry.test/package", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_wait_for_a_rate_limit_slot_ends_when_its_work_is_cancelled_and_frees_the_key()
    {
        var limiter = new RateLimiterService(
            NullLogger<RateLimiterService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimitSettings:GitHubScrapingMaxRequests"] = "1",
                ["RateLimitSettings:GitHubScrapingWindowSeconds"] = "600"
            }).Build());
        await limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));

        for (var i = 0; i < 2; i++)
        {
            using var work = new CancellationTokenSource();
            Task<int> waiting;
            using (WorkCancellation.Use(work.Token))
                waiting = limiter.ExecuteAsync("github-scraping", () => Task.FromResult(0));
            work.Cancel();

            // The second round would hang here if the first cancelled wait had kept the gate of the key
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public bool Answer { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Answer)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
