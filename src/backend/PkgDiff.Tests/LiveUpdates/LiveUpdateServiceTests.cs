using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.LiveUpdates;

public class LiveUpdateServiceTests
{
    private static readonly Registry NpmRegistry = new() { Id = 1, Value = "npm", Label = "npm" };
    private static readonly Registry NugetRegistry = new() { Id = 2, Value = "nuget", Label = "NuGet" };
    private static readonly DateTime Day = new(2026, 9, 1, 12, 0, 0);

    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingProgressReporter _reporter = new();
    private readonly LiveUpdateService _service;

    public LiveUpdateServiceTests()
    {
        _service = new LiveUpdateService(_unitOfWork, TestRegistries.Create(), _reporter, NullLogger<LiveUpdateService>.Instance);
    }

    private async Task<CachedPackage> AddPackageAsync(
        string packageId,
        string latestVersion,
        DateTime? latestVersionDate = null,
        DateTime? cachedAt = null,
        Registry? registry = null)
    {
        registry ??= NpmRegistry;
        return await _unitOfWork.CachedPackages.AddAsync(new CachedPackage
        {
            PackageId = packageId,
            Registry = registry,
            RegistryId = registry.Id,
            LatestVersion = latestVersion,
            LatestVersionDate = latestVersionDate,
            CachedAt = cachedAt ?? Day
        });
    }

    [Fact]
    public async Task Feed_lists_cached_packages_newest_first_and_falls_back_to_the_cache_time()
    {
        await AddPackageAsync("old", "1.0.0", latestVersionDate: Day.AddDays(-30));
        await AddPackageAsync("newest", "3.1.0", latestVersionDate: Day.AddDays(2), registry: NugetRegistry);
        await AddPackageAsync("never-refreshed", "0.9.0", cachedAt: Day.AddDays(1));
        await AddPackageAsync("no-version", "");

        var feed = await _service.GetLiveUpdatesAsync();

        Assert.Equal(new[] { "newest", "never-refreshed", "old" }, feed.Data.Select(u => u.PackageId));
        Assert.Equal(3, feed.Total);

        var first = feed.Data[0];
        Assert.Equal("nuget", first.Registry);
        Assert.Equal("3.1.0", first.Version);
        Assert.Equal(Day.AddDays(2), first.UpdatedAt);
        Assert.All(feed.Data, u => Assert.Equal(DateTimeKind.Utc, u.UpdatedAt.Kind));
    }

    [Fact]
    public async Task Feed_is_paged_on_the_server_side_and_clamps_its_arguments()
    {
        for (var i = 1; i <= 5; i++)
            await AddPackageAsync($"pkg-{i}", "1.0.0", latestVersionDate: Day.AddDays(i));

        var second = await _service.GetLiveUpdatesAsync(page: 2, pageSize: 2);
        Assert.Equal(new[] { "pkg-3", "pkg-2" }, second.Data.Select(u => u.PackageId));
        Assert.Equal(5, second.Total);
        Assert.Equal(3, second.TotalPages);

        var clamped = await _service.GetLiveUpdatesAsync(page: 0, pageSize: 5000);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(50, clamped.PageSize);
        Assert.Equal(5, clamped.Data.Count);
    }

    [Fact]
    public async Task Packages_with_the_same_date_keep_one_order_across_pages()
    {
        for (var i = 1; i <= 4; i++)
            await AddPackageAsync($"pkg-{i}", "1.0.0", latestVersionDate: Day);

        var first = await _service.GetLiveUpdatesAsync(page: 1, pageSize: 2);
        var second = await _service.GetLiveUpdatesAsync(page: 2, pageSize: 2);

        Assert.Equal(new[] { "pkg-4", "pkg-3", "pkg-2", "pkg-1" }, first.Data.Concat(second.Data).Select(u => u.PackageId));
    }

    [Fact]
    public async Task A_newer_version_is_stored_with_its_publish_date_and_pushed_once()
    {
        var package = await AddPackageAsync("left-pad", "1.2.0", latestVersionDate: Day.AddDays(-10));
        var published = Day.AddHours(3);

        var changed = await _service.RecordLatestVersionAsync(package, "NPM", "1.3.0", published);

        Assert.True(changed);
        Assert.Equal("1.3.0", package.LatestVersion);
        Assert.Equal("1.2.0", package.PreviousLatestVersion);
        Assert.Equal(published, package.LatestVersionDate);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);

        var pushed = Assert.Single(_reporter.LiveUpdates);
        Assert.Equal(("left-pad", "npm", "1.3.0", "1.2.0"), (pushed.PackageId, pushed.Registry, pushed.Version, pushed.PreviousVersion));
        Assert.Equal(published, pushed.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, pushed.UpdatedAt.Kind);
    }

    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("2024a", "2024A")]
    public async Task The_same_version_is_not_news_but_fills_in_a_missing_date(string current, string reported)
    {
        var package = await AddPackageAsync("left-pad", current);

        var changed = await _service.RecordLatestVersionAsync(package, "npm", reported, Day);

        Assert.False(changed);
        Assert.Equal(current, package.LatestVersion);
        Assert.Null(package.PreviousLatestVersion);
        Assert.Equal(Day, package.LatestVersionDate);
        Assert.Empty(_reporter.LiveUpdates);

        await _service.RecordLatestVersionAsync(package, "npm", reported, Day.AddDays(5));
        Assert.Equal(Day, package.LatestVersionDate);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task An_older_version_is_ignored_unless_the_registry_itself_reports_it()
    {
        var package = await AddPackageAsync("left-pad", "2.0.0-beta.1", latestVersionDate: Day);

        Assert.False(await _service.RecordLatestVersionAsync(package, "npm", "1.9.0", Day.AddDays(-3)));
        Assert.Equal("2.0.0-beta.1", package.LatestVersion);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);

        Assert.False(await _service.RecordLatestVersionAsync(package, "npm", "1.9.0", Day.AddDays(-3), authoritative: true));
        Assert.Equal("1.9.0", package.LatestVersion);
        Assert.Null(package.PreviousLatestVersion);
        Assert.Equal(Day.AddDays(-3), package.LatestVersionDate);
        Assert.Empty(_reporter.LiveUpdates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1900-01-01T00:00:00")]
    [InlineData("2999-01-01T00:00:00")]
    public async Task A_missing_or_implausible_publish_date_becomes_the_detection_time(string? reported)
    {
        var package = await AddPackageAsync("left-pad", "1.2.0", latestVersionDate: Day);
        var before = DateTime.UtcNow;

        await _service.RecordLatestVersionAsync(package, "npm", "1.3.0", reported == null ? null : DateTime.Parse(reported));

        Assert.InRange(package.LatestVersionDate!.Value, before, DateTime.UtcNow);
        Assert.Single(_reporter.LiveUpdates);
    }

    [Fact]
    public async Task A_local_publish_date_is_stored_as_utc()
    {
        var package = await AddPackageAsync("left-pad", "1.2.0");
        var local = DateTime.SpecifyKind(Day, DateTimeKind.Local);

        await _service.RecordLatestVersionAsync(package, "npm", "1.3.0", local);

        Assert.Equal(local.ToUniversalTime(), package.LatestVersionDate);
    }

    [Fact]
    public async Task The_first_known_version_of_a_package_is_stored_without_a_push()
    {
        var package = await AddPackageAsync("left-pad", "");

        var changed = await _service.RecordLatestVersionAsync(package, "npm", "1.0.0", Day);

        Assert.False(changed);
        Assert.Equal("1.0.0", package.LatestVersion);
        Assert.Null(package.PreviousLatestVersion);
        Assert.Equal(Day, package.LatestVersionDate);
        Assert.Empty(_reporter.LiveUpdates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.0.0-a-version-string-that-is-longer-than-the-fifty-character-column")]
    public async Task An_empty_or_oversized_version_changes_nothing(string reported)
    {
        var package = await AddPackageAsync("left-pad", "1.2.0", latestVersionDate: Day);

        Assert.False(await _service.RecordLatestVersionAsync(package, "npm", reported, Day.AddDays(1), authoritative: true));

        Assert.Equal("1.2.0", package.LatestVersion);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task A_failed_push_does_not_lose_the_stored_version()
    {
        var package = await AddPackageAsync("left-pad", "1.2.0");
        _reporter.BroadcastException = new InvalidOperationException("hub is down");

        var changed = await _service.RecordLatestVersionAsync(package, "npm", "1.3.0", Day);

        Assert.True(changed);
        Assert.Equal("1.3.0", package.LatestVersion);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task A_metadata_refresh_that_finds_a_new_version_updates_the_package_and_pushes_it()
    {
        await _unitOfWork.Registries.AddAsync(NugetRegistry);
        var package = await AddPackageAsync("My.Pkg", "1.0.0", latestVersionDate: Day.AddDays(-20), registry: NugetRegistry);
        var published = Day.AddHours(1);
        var nuget = new StubNugetService(new PackageMetadata
        {
            PackageId = "My.Pkg",
            Registry = "nuget",
            LatestVersion = "1.1.0",
            Versions =
            [
                new VersionSummary { Version = "1.1.0", PublishedDate = published },
                new VersionSummary { Version = "1.0.0", PublishedDate = Day.AddDays(-20) }
            ]
        });
        var cache = new PackageCacheService(
            _unitOfWork, TestRegistries.Create(nuget: nuget), new ReleaseNotesRenderer(), _service, new CachedLookups(_unitOfWork, new MemoryCache(new MemoryCacheOptions())), null!, NullLogger<PackageCacheService>.Instance);

        var metadata = await cache.GetOrFetchMetadataAsync("nuget", "My.Pkg", bypassCache: true);

        Assert.Equal("1.1.0", metadata.LatestVersion);
        Assert.Equal("1.1.0", package.LatestVersion);
        Assert.Equal("1.0.0", package.PreviousLatestVersion);
        Assert.Equal(published, package.LatestVersionDate);

        var pushed = Assert.Single(_reporter.LiveUpdates);
        Assert.Equal(("My.Pkg", "nuget", "1.1.0"), (pushed.PackageId, pushed.Registry, pushed.Version));

        await cache.GetOrFetchMetadataAsync("nuget", "My.Pkg", bypassCache: true);
        Assert.Single(_reporter.LiveUpdates);
    }

    private sealed class StubNugetService(PackageMetadata metadata) : INugetService
    {
        public Task<PackageMetadata> GetPackageMetadataAsync(string packageId) => Task.FromResult(metadata);

        public Task<IEnumerable<NuGetVersion>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false) =>
            throw new NotSupportedException();

        public Task<IEnumerable<NuGetVersion>> GetVersionsInRangeAsync(
            string packageId, NuGetVersion fromVersion, NuGetVersion toVersion, bool includePrerelease = false) =>
            throw new NotSupportedException();

        public Task<bool> PackageExistsAsync(string packageId) => throw new NotSupportedException();

        public Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version) =>
            throw new NotSupportedException();
    }
}
