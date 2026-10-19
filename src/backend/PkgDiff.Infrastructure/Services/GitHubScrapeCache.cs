using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace PkgDiff.Infrastructure.Services;

// What the GitHub scraper remembers between requests. These used to be static dictionaries that
// only ever grew and never forgot; here every entry expires and the number of entries is capped.
public sealed class GitHubScrapeCache : IDisposable
{
    private const int MaxEntries = 2000;

    // A background scrape asks for the releases of a repository once per version of the package, so
    // the list has to outlive a whole scrape; an empty list is kept as long, or every version of a
    // package whose repository publishes no releases would ask GitHub again
    private static readonly TimeSpan ReleasesLifetime = TimeSpan.FromHours(3);

    // What GitHub did not answer (an error, a page that failed) is asked again, but not for every version
    private static readonly TimeSpan UnansweredLifetime = TimeSpan.FromMinutes(15);

    // A repository that had no changelog file may get one, so this knowledge is not kept for good
    private static readonly TimeSpan ChangelogLifetime = TimeSpan.FromHours(12);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    public bool TryGetReleases(string repositoryUrl, [NotNullWhen(true)] out Dictionary<string, string>? releases)
    {
        return _cache.TryGetValue(ReleasesKey(repositoryUrl), out releases) && releases != null;
    }

    public void SetReleases(string repositoryUrl, Dictionary<string, string> releases, bool answered = true)
    {
        _cache.Set(ReleasesKey(repositoryUrl), releases, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = answered ? ReleasesLifetime : UnansweredLifetime,
            Size = 1
        });
    }

    // Closed milestones of a repository by version; an empty map for a repository without any
    public bool TryGetMilestones(string repoKey, [NotNullWhen(true)] out Dictionary<string, int>? milestones)
    {
        return _cache.TryGetValue("milestones:" + repoKey, out milestones) && milestones != null;
    }

    public void SetMilestones(string repoKey, Dictionary<string, int> milestones, bool answered = true)
    {
        _cache.Set("milestones:" + repoKey, milestones, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = answered ? ChangelogLifetime : UnansweredLifetime,
            Size = 1
        });
    }

    // Per-version release files of a repository (version -> path); an empty map for a repository without any
    public bool TryGetReleaseFiles(string repoKey, [NotNullWhen(true)] out Dictionary<string, string>? files)
    {
        return _cache.TryGetValue("release-files:" + repoKey, out files) && files != null;
    }

    public void SetReleaseFiles(string repoKey, Dictionary<string, string> files)
    {
        _cache.Set("release-files:" + repoKey, files, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ChangelogLifetime,
            Size = 1
        });
    }

    public RepoChangelogKnowledge Changelog(string repoKey)
    {
        return _cache.GetOrCreate("changelog:" + repoKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ChangelogLifetime;
            entry.Size = 1;
            return new RepoChangelogKnowledge();
        })!;
    }

    public void Dispose() => _cache.Dispose();

    private static string ReleasesKey(string repositoryUrl) => "releases:" + repositoryUrl;
}

// Which changelog files a repository has, as far as earlier lookups could tell. Shared between
// requests, so every access is locked.
public sealed class RepoChangelogKnowledge
{
    public const int FailuresBeforeMissing = 3;

    private readonly object _gate = new();
    private readonly List<string> _existing = new();
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private bool _knowsExisting;
    private bool _fullyResolved;

    // True once the repository's file list was read through the API: the existing files are then complete
    public bool IsFullyResolved
    {
        get
        {
            lock (_gate)
                return _fullyResolved;
        }
    }

    // The files known to exist; null while nothing is known yet
    public List<string>? ExistingFiles()
    {
        lock (_gate)
            return _knowsExisting ? new List<string>(_existing) : null;
    }

    public void SetResolved(IEnumerable<string> files)
    {
        lock (_gate)
        {
            _existing.Clear();
            _existing.AddRange(files);
            _knowsExisting = true;
            _fullyResolved = true;
        }
    }

    public void AddExisting(string fileName)
    {
        lock (_gate)
        {
            if (!_existing.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                _existing.Add(fileName);
            _knowsExisting = true;
        }
    }

    public bool IsMissing(string fileName)
    {
        lock (_gate)
            return _missing.Contains(fileName);
    }

    public void MarkMissing(string fileName)
    {
        lock (_gate)
            _missing.Add(fileName);
    }

    // Counts one more lookup of this file that yielded nothing; after enough of them the file counts as missing
    public int RecordFailure(string fileName)
    {
        lock (_gate)
        {
            var failures = _failures.GetValueOrDefault(fileName) + 1;
            _failures[fileName] = failures;
            if (failures >= FailuresBeforeMissing)
                _missing.Add(fileName);
            return failures;
        }
    }
}
