using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Outbound;

public class GitHubScrapeCacheTests
{
    [Fact]
    public void Releases_are_returned_for_the_repository_they_were_stored_for()
    {
        using var cache = new GitHubScrapeCache();
        var releases = new Dictionary<string, string> { ["1.0.0"] = "notes" };

        cache.SetReleases("https://github.com/o/r", releases);

        Assert.True(cache.TryGetReleases("https://github.com/o/r", out var cached));
        Assert.Same(releases, cached);
        Assert.False(cache.TryGetReleases("https://github.com/o/other", out _));
    }

    [Fact]
    public void Requests_for_one_repository_share_what_is_known_about_its_changelog()
    {
        using var cache = new GitHubScrapeCache();

        cache.Changelog("o/r").AddExisting("CHANGELOG.md");

        Assert.Equal(new[] { "CHANGELOG.md" }, cache.Changelog("o/r").ExistingFiles());
        Assert.Null(cache.Changelog("o/other").ExistingFiles());
    }

    [Fact]
    public void Nothing_is_known_about_a_repository_until_a_lookup_says_so()
    {
        var knowledge = new RepoChangelogKnowledge();

        Assert.Null(knowledge.ExistingFiles());
        Assert.False(knowledge.IsFullyResolved);
        Assert.False(knowledge.IsMissing("CHANGELOG.md"));
    }

    [Fact]
    public void A_file_seen_once_is_remembered_without_claiming_the_list_is_complete()
    {
        var knowledge = new RepoChangelogKnowledge();

        knowledge.AddExisting("CHANGELOG.md");
        knowledge.AddExisting("changelog.md");

        Assert.Equal(new[] { "CHANGELOG.md" }, knowledge.ExistingFiles());
        Assert.False(knowledge.IsFullyResolved);
    }

    [Fact]
    public void The_file_list_from_the_api_is_complete_even_when_it_is_empty()
    {
        var knowledge = new RepoChangelogKnowledge();
        knowledge.AddExisting("HISTORY.md");

        knowledge.SetResolved([]);

        Assert.True(knowledge.IsFullyResolved);
        Assert.Empty(knowledge.ExistingFiles()!);
    }

    [Fact]
    public void A_file_counts_as_missing_after_three_lookups_that_found_nothing()
    {
        var knowledge = new RepoChangelogKnowledge();

        Assert.Equal(1, knowledge.RecordFailure("CHANGES.md"));
        Assert.Equal(2, knowledge.RecordFailure("changes.md"));
        Assert.False(knowledge.IsMissing("CHANGES.md"));

        Assert.Equal(3, knowledge.RecordFailure("CHANGES.md"));
        Assert.True(knowledge.IsMissing("changes.md"));
    }

    [Fact]
    public async Task Knowledge_can_be_read_and_changed_from_many_requests_at_once()
    {
        var knowledge = new RepoChangelogKnowledge();

        var work = Enumerable.Range(0, 32).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                knowledge.AddExisting($"file-{i % 20}.md");
                knowledge.MarkMissing($"missing-{worker}-{i}.md");
                knowledge.RecordFailure($"flaky-{i % 7}.md");
                knowledge.ExistingFiles();
                knowledge.IsMissing("file-1.md");
            }
        }));
        await Task.WhenAll(work);

        Assert.Equal(20, knowledge.ExistingFiles()!.Count);
    }
}
