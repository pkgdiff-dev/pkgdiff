using PkgDiff.Application.Services;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Tests.Coalescing;

public class WorkTrackerServiceTests
{
    private readonly WorkTrackerService _tracker = new();

    private static AnalyzeRequest Request(
        string registry = "npm", string packageId = "left-pad", string from = "1.0.0", string to = "2.0.0") =>
        new() { Registry = registry, PackageId = packageId, CurrentVersion = from, TargetVersion = to };

    [Fact]
    public async Task Callers_with_the_same_key_share_one_run_and_its_result()
    {
        var runs = 0;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task<string>> work = () =>
        {
            Interlocked.Increment(ref runs);
            return release.Task;
        };

        // A call has joined the run once it returns its task, so none can arrive after the run ends
        var callers = new Task<string>[50];
        Parallel.For(0, callers.Length, i => callers[i] = _tracker.RunCoalescedAsync("key", work));
        Assert.True(_tracker.IsRunning("key"));
        release.SetResult("done");
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, runs);
        Assert.All(results, result => Assert.Equal("done", result));
        Assert.False(_tracker.IsRunning("key"));
    }

    [Fact]
    public async Task Different_keys_run_separately_and_return_their_own_results()
    {
        var npm = _tracker.RunCoalescedAsync(WorkKeys.Analysis(Request("npm")), () => Task.FromResult("npm result"));
        var pypi = _tracker.RunCoalescedAsync(WorkKeys.Analysis(Request("pypi")), () => Task.FromResult("pypi result"));

        Assert.Equal("npm result", await npm);
        Assert.Equal("pypi result", await pypi);
    }

    [Fact]
    public async Task A_finished_or_failed_run_is_forgotten_so_the_next_call_runs_again()
    {
        var runs = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _tracker.RunCoalescedAsync<int>("key", () =>
        {
            runs++;
            throw new InvalidOperationException("registry is down");
        }));
        Assert.False(_tracker.IsRunning("key"));

        Assert.Equal(7, await _tracker.RunCoalescedAsync("key", () => Task.FromResult(7 + runs - 1)));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task A_caller_that_finishes_late_does_not_evict_a_newer_run_of_the_same_key()
    {
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstRun = _tracker.RunCoalescedAsync("key", () => first.Task);
        first.SetResult(1);
        Assert.Equal(1, await firstRun);

        var secondRun = _tracker.RunCoalescedAsync("key", () => second.Task);
        Assert.True(_tracker.IsRunning("key"));

        Assert.Equal(3, await _tracker.RunCoalescedAsync("other", () => Task.FromResult(3)));
        Assert.True(_tracker.IsRunning("key"));

        second.SetResult(2);
        Assert.Equal(2, await secondRun);
        Assert.False(_tracker.IsRunning("key"));
    }

    [Fact]
    public async Task The_same_key_with_another_result_type_runs_on_its_own_instead_of_failing()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var text = _tracker.RunCoalescedAsync("key", () => release.Task);

        Assert.Equal(42, await _tracker.RunCoalescedAsync("key", () => Task.FromResult(42)));

        release.SetResult("done");
        Assert.Equal("done", await text);
    }

    [Fact]
    public async Task Active_work_is_counted_by_key_prefix()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = new[]
        {
            _tracker.RunCoalescedAsync(WorkKeys.Analysis(Request("npm")), () => release.Task),
            _tracker.RunCoalescedAsync(WorkKeys.Analysis(Request("pypi")), () => release.Task),
            _tracker.RunCoalescedAsync(WorkKeys.Refresh(Request("npm")), () => release.Task),
            _tracker.RunCoalescedAsync(WorkKeys.Scrape("npm", "left-pad"), () => release.Task)
        };

        Assert.Equal(2, _tracker.GetActiveCount(WorkKeys.AnalysisPrefix));

        release.SetResult(0);
        await Task.WhenAll(runs);
        Assert.Equal(0, _tracker.GetActiveCount(WorkKeys.AnalysisPrefix));
    }

    [Fact]
    public void An_analysis_key_names_registry_package_range_and_the_prerelease_flag()
    {
        var key = WorkKeys.Analysis(Request());

        Assert.Equal(key, WorkKeys.Analysis(Request(registry: "NPM", packageId: "Left-Pad")));
        Assert.NotEqual(key, WorkKeys.Analysis(Request(registry: "pypi")));
        Assert.NotEqual(key, WorkKeys.Analysis(Request(packageId: "right-pad")));
        Assert.NotEqual(key, WorkKeys.Analysis(Request(from: "1.5.0")));
        Assert.NotEqual(key, WorkKeys.Analysis(Request(to: "3.0.0")));
        Assert.NotEqual(key, WorkKeys.Refresh(Request()));

        var withPrerelease = Request();
        withPrerelease.IncludePrerelease = true;
        Assert.NotEqual(key, WorkKeys.Analysis(withPrerelease));
    }
}
