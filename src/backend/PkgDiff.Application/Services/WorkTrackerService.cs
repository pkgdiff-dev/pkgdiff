using System.Collections.Concurrent;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Application.Services;

public class WorkTrackerService : IWorkTrackerService
{
    private sealed class Entry
    {
        public required Type ResultType { get; init; }
        public required Lazy<Task> Work { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
        public int Waiters;
    }

    private readonly ConcurrentDictionary<string, Entry> _active = new();

    public int GetActiveCount(string keyPrefix)
    {
        return _active.Keys.Count(key => key.StartsWith(keyPrefix, StringComparison.Ordinal));
    }

    public bool IsRunning(string key)
    {
        return _active.ContainsKey(key);
    }

    public async Task<T> RunCoalescedAsync<T>(string key, Func<Task<T>> work)
    {
        var callerToken = WorkCancellation.Token;

        while (true)
        {
            // GetOrAdd may build several entries under a race, but stores one; Lazy starts the work of that one only
            var entry = _active.GetOrAdd(key, _ => NewEntry(work));

            // The same key with another result type has nothing to share
            if (entry.ResultType != typeof(T))
                return await work();

            Interlocked.Increment(ref entry.Waiters);
            try
            {
                // A caller that leaves stops waiting; the shared run itself has its own token
                return await ((Task<T>)entry.Work.Value).WaitAsync(callerToken);
            }
            catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && entry.Cancellation.IsCancellationRequested)
            {
                // The run was cancelled because its other callers left just as this one joined: start again
            }
            finally
            {
                var nobodyWaits = Interlocked.Decrement(ref entry.Waiters) == 0;
                if (entry.Work.Value.IsCompleted || nobodyWaits)
                {
                    // Removes this entry only: a caller that finishes late must not evict a newer run of the same key
                    _active.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                }

                // Every caller is gone and the run is still going: nobody wants its result any more
                if (nobodyWaits && !entry.Work.Value.IsCompleted)
                    entry.Cancellation.Cancel();
            }
        }
    }

    private static Entry NewEntry<T>(Func<Task<T>> work)
    {
        Entry? entry = null;
        entry = new Entry
        {
            ResultType = typeof(T),
            Work = new Lazy<Task>(() => Task.Run(async () =>
            {
                // The run does not belong to the request that started it, so it does not use that request's token
                using (WorkCancellation.Use(entry!.Cancellation.Token))
                    return await work();
            }))
        };
        return entry;
    }
}
