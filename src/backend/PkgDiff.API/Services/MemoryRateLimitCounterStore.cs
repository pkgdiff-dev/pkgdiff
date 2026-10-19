using System.Collections.Concurrent;

namespace PkgDiff.API.Services;

// Fixed windows: the first request of a key starts its window, and the window ends when its time is up,
// however many requests arrive in between
public class MemoryRateLimitCounterStore : IRateLimitCounterStore
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private sealed class Window
    {
        public int Count;
        public DateTime ExpiresAt;
        public bool Removed;
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private long _nextSweepTicks = DateTime.UtcNow.Add(SweepInterval).Ticks;

    public Task<int> IncrementAsync(string key, TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        Sweep(now);

        while (true)
        {
            var window = _windows.GetOrAdd(key, _ => new Window { ExpiresAt = now + ttl });
            lock (window)
            {
                // The sweep took this window out of the dictionary; count on a fresh one
                if (window.Removed)
                    continue;

                if (window.ExpiresAt <= now)
                {
                    window.Count = 0;
                    window.ExpiresAt = now + ttl;
                }

                return Task.FromResult(++window.Count);
            }
        }
    }

    public int? GetCount(string key)
    {
        if (!_windows.TryGetValue(key, out var window))
            return null;

        lock (window)
        {
            return window.Removed || window.ExpiresAt <= DateTime.UtcNow ? null : window.Count;
        }
    }

    // Keeps the dictionary from growing with every address ever seen
    private void Sweep(DateTime now)
    {
        var due = Interlocked.Read(ref _nextSweepTicks);
        if (now.Ticks < due || Interlocked.CompareExchange(ref _nextSweepTicks, now.Add(SweepInterval).Ticks, due) != due)
            return;

        foreach (var entry in _windows)
        {
            lock (entry.Value)
            {
                if (entry.Value.ExpiresAt > now)
                    continue;

                entry.Value.Removed = true;
                _windows.TryRemove(entry);
            }
        }
    }
}
