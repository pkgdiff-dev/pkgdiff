using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.API.Controllers;
using PkgDiff.API.Services;
using PkgDiff.Application.Jobs;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Common;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Infrastructure.Repositories;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Postgres;

public class PostgresQueryTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static async Task<(Registry Npm, Registry Pypi)> SeedRegistriesAsync(ApplicationDbContext context)
    {
        var npm = new Registry { Value = "npm", Label = "npm", DisplayOrder = 1 };
        var pypi = new Registry { Value = "pypi", Label = "PyPI", DisplayOrder = 2 };
        context.Registries.AddRange(npm, pypi);
        await context.SaveChangesAsync();
        return (npm, pypi);
    }

    private static CachedPackage Package(string id, Registry registry, string latest, DateTime cachedAt, DateTime? latestDate = null) => new()
    {
        PackageId = id,
        RegistryId = registry.Id,
        LatestVersion = latest,
        LatestVersionDate = latestDate,
        CachedAt = cachedAt,
        ExpiresAt = cachedAt.AddDays(14)
    };

    private static CachedReleaseNote Note(CachedPackage package, string version, string notes = "notes") => new()
    {
        CachedPackageId = package.Id,
        Version = version,
        ReleaseNotes = notes,
        IsEmpty = notes.Length == 0,
        CachedAt = Now,
        ExpiresAt = Now.AddDays(90)
    };

    [Fact]
    public async Task Every_migration_applies_to_an_empty_database_and_matches_the_model()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Dashboard_search_history_and_package_queries_run_in_the_database()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var (npm, pypi) = await SeedRegistriesAsync(context);
            var leftPad = Package("Left-Pad", npm, "1.3.0", Now.AddDays(-1));
            var requests = Package("requests", pypi, "2.32.0", Now);
            var empty = Package("no-version", npm, "", Now.AddDays(-2));
            context.CachedPackages.AddRange(leftPad, requests, empty);
            await context.SaveChangesAsync();
            context.CachedReleaseNotes.AddRange(Note(leftPad, "1.2.0"), Note(leftPad, "1.3.0"), Note(requests, "2.32.0"));

            for (var i = 0; i < 3; i++)
                context.SearchHistories.Add(new SearchHistory { PackageId = "left-pad", FromVersion = "1.0.0", ToVersion = "1.3.0", IpAddress = "1.1.1.1", SearchedAt = Now.AddHours(-i) });
            context.SearchHistories.Add(new SearchHistory { PackageId = "requests", FromVersion = "2.0", ToVersion = "2.32.0", IpAddress = "2.2.2.2", SearchedAt = Now.AddDays(-10) });
            context.SearchHistories.Add(new SearchHistory { PackageId = "refreshed", IpAddress = "System", SearchedAt = Now });
            await context.SaveChangesAsync();
        }

        using var unitOfWork = database.CreateUnitOfWork();
        var dashboard = new AdminDashboardService(unitOfWork);
        var admin = new AdminPackageService(unitOfWork);

        var stats = await dashboard.GetDashboardStatsAsync();
        Assert.Equal(4, stats.TotalSearches);
        Assert.Equal(2, stats.TotalPackagesAnalyzed);
        Assert.Equal(3, stats.CachedPackages);
        Assert.Equal(new[] { ("left-pad", 3), ("requests", 1) }, stats.TopPackages.Select(p => (p.PackageId, p.SearchCount)));
        Assert.Equal(4, stats.RecentSearches.Count);
        Assert.Equal("1.0.0 → 1.3.0", stats.RecentSearches[0].VersionRange);
        Assert.Equal(3, stats.SearchesByDay.Values.Sum());

        var history = await dashboard.GetSearchHistoryAsync(page: 2, pageSize: 3);
        Assert.Equal(4, history.Total);
        Assert.Equal("requests", Assert.Single(history.Data).PackageId);

        var packages = await admin.GetPackagesAsync(search: "LEFT");
        var listed = Assert.Single(packages.Data);
        Assert.Equal("Left-Pad", listed.PackageId);
        Assert.Equal(2, listed.ReleaseNotesCount);
        Assert.Equal(3, (await admin.GetPackagesAsync(page: 0, pageSize: 5000)).Total);

        var sitemap = await admin.GetPackagesForSitemapAsync();
        Assert.Equal(new[] { "npm/Left-Pad", "npm/no-version", "pypi/requests" }, sitemap.Select(e => $"{e.Registry}/{e.PackageId}"));

        var lookups = new CachedLookups(unitOfWork, new MemoryCache(new MemoryCacheOptions()));
        var live = new LiveUpdateService(unitOfWork, TestRegistries.Create(), new RecordingProgressReporter(), NullLogger<LiveUpdateService>.Instance);
        var cache = new PackageCacheService(
            unitOfWork, TestRegistries.Create(), new ReleaseNotesRenderer(), live, lookups, null!, NullLogger<PackageCacheService>.Instance);
        var analysis = new PackageAnalysisService(
            null!, cache, null!, unitOfWork, null!, null!, null!, null!, null!, live, lookups, null!, null!, NullLogger<PackageAnalysisService>.Instance);

        var cached = await analysis.GetCachedPackagesAsync();
        Assert.Equal(new[] { "pypi/requests", "npm/Left-Pad" }, cached.Select(p => $"{p.Registry}/{p.PackageId}"));

        var suggestions = await analysis.SearchSuggestionsAsync("npm", "left", 5);
        Assert.Equal("Left-Pad", Assert.Single(suggestions).PackageId);

        var feed = await live.GetLiveUpdatesAsync(page: 1, pageSize: 1);
        Assert.Equal(2, feed.Total);
        Assert.Equal(("requests", "pypi"), (feed.Data[0].PackageId, feed.Data[0].Registry));
    }

    [Fact]
    public async Task Scrape_log_and_security_queries_run_in_the_database()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            context.ScrapeLogs.AddRange(
                new ScrapeLog { PackageId = "Flaky.Pkg", Version = "1.0", FailureReason = "old reason", CreatedAt = Now.AddDays(-2) },
                new ScrapeLog { PackageId = "Flaky.Pkg", Version = "1.1", FailureReason = "latest reason", CreatedAt = Now.AddHours(-1) },
                new ScrapeLog { PackageId = "other", Version = "2.0", FailureReason = "404", CreatedAt = Now.AddDays(-1) },
                new ScrapeLog { PackageId = "fixed", Version = "3.0", FailureReason = "done", IsResolved = true, CreatedAt = Now });

            for (var i = 0; i < 5; i++)
                context.RequestLogs.Add(new RequestLog { IpAddress = "9.9.9.9", Endpoint = i < 3 ? "/api/package/analyze" : "/api/package/recent", Method = "GET", StatusCode = 200, CreatedAt = Now.AddMinutes(-i) });
            context.RequestLogs.Add(new RequestLog { IpAddress = "2001:DB8::1", Endpoint = "/", Method = "GET", StatusCode = 200, CreatedAt = Now.AddMinutes(-30) });

            context.BlockedIps.AddRange(
                new BlockedIp { IpAddress = "9.9.9.9", Reason = "abuse", BlockType = BlockType.Permanent, CreatedAt = Now },
                new BlockedIp { IpAddress = "8.8.8.8", Reason = "burst", BlockType = BlockType.Temporary, ExpiresAt = Now.AddHours(1), CreatedAt = Now.AddMinutes(-5) },
                new BlockedIp { IpAddress = "7.7.7.7", Reason = "expired", BlockType = BlockType.Temporary, ExpiresAt = Now.AddHours(-1), CreatedAt = Now.AddDays(-1) });
            await context.SaveChangesAsync();
        }

        using var unitOfWork = database.CreateUnitOfWork();
        var scrapeLogs = new AdminScrapeLogService(unitOfWork);
        var admin = new AdminSecurityService(unitOfWork);

        var logs = await scrapeLogs.GetScrapeLogsAsync(packageFilter: "flaky");
        Assert.Equal(new[] { "1.1", "1.0" }, logs.Data.Select(l => l.Version));

        var summary = await scrapeLogs.GetScrapeLogSummaryAsync();
        Assert.Equal(4, summary.TotalFailures);
        Assert.Equal(3, summary.UnresolvedCount);
        Assert.Equal(new[] { "Flaky.Pkg", "other" }, summary.TopFailingPackages.Select(p => p.PackageId));
        Assert.Equal(("latest reason", 2), (summary.TopFailingPackages[0].LatestReason, summary.TopFailingPackages[0].FailureCount));

        var requestLogs = await admin.GetRequestLogsAsync(ipFilter: "2001:db8");
        Assert.Equal("2001:DB8::1", Assert.Single(requestLogs.Data).IpAddress);
        Assert.Equal(6, (await admin.GetRequestLogsAsync()).Total);

        var blocked = await admin.GetBlockedIpsAsync();
        Assert.Equal(2, blocked.Total);
        Assert.Equal(new[] { ("9.9.9.9", "Permanent"), ("8.8.8.8", "Temporary") }, blocked.Data.Select(b => (b.IpAddress, b.BlockType)));

        var statistics = await admin.GetIpStatisticsAsync(topCount: 1);
        var busiest = Assert.Single(statistics);
        Assert.Equal(("9.9.9.9", 5, true), (busiest.IpAddress, busiest.RequestCount, busiest.IsBlocked));
        Assert.Equal(new[] { "/api/package/analyze", "/api/package/recent" }, busiest.TopEndpoints);

        await scrapeLogs.ClearResolvedScrapeLogsAsync();
        Assert.Equal(3, await unitOfWork.ScrapeLogs.CountAsync());
        Assert.True(await admin.IsIpBlockedAsync("9.9.9.9"));
        Assert.False(await admin.IsIpBlockedAsync("7.7.7.7"));
    }

    [Fact]
    public async Task A_second_note_for_one_version_is_refused_and_written_onto_the_existing_row()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        int packageId;
        await using (var context = database.CreateContext())
        {
            var (npm, _) = await SeedRegistriesAsync(context);
            var package = Package("left-pad", npm, "1.3.0", Now);
            context.CachedPackages.Add(package);
            await context.SaveChangesAsync();
            context.CachedReleaseNotes.Add(Note(package, "1.3.0", notes: ""));
            await context.SaveChangesAsync();
            packageId = package.Id;
        }

        using var unitOfWork = database.CreateUnitOfWork();
        await unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote { CachedPackageId = packageId, Version = "1.3.0", ReleaseNotes = "dup", CachedAt = Now, ExpiresAt = Now });
        await Assert.ThrowsAsync<DuplicateKeyException>(() => unitOfWork.SaveChangesAsync());

        Assert.Equal(0, await unitOfWork.SaveChangesAsync());

        var cache = new PackageCacheService(
            unitOfWork, null!, new ReleaseNotesRenderer(), null!, null!, null!, NullLogger<PackageCacheService>.Instance);
        await cache.SaveReleaseNoteAsync(new CachedReleaseNote
        {
            CachedPackageId = packageId,
            Version = "1.3.0",
            ReleaseNotes = "- Fixed a crash",
            ReleaseNotesSource = "GitHub",
            CachedAt = Now,
            ExpiresAt = Now.AddDays(90)
        });

        await using var verify = database.CreateContext();
        var stored = await verify.CachedReleaseNotes.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("- Fixed a crash", "GitHub", false), (stored.ReleaseNotes, stored.ReleaseNotesSource, stored.IsEmpty));
    }

    [Fact]
    public async Task The_refresh_job_and_the_blog_lists_read_only_what_they_need()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var (npm, pypi) = await SeedRegistriesAsync(context);
            context.CachedPackages.AddRange(Package("left-pad", npm, "1.3.0", Now.AddDays(-1)), Package("requests", pypi, "2.32.0", Now));
            context.BlogPosts.AddRange(
                new BlogPost { Title = "Old", Slug = "old", PackageId = "left-pad", Version = "1.2.0", Registry = "npm", Content = new string('x', 500), IsPublished = true, CreatedAt = Now.AddDays(-9), PublishedAt = Now.AddDays(-8) },
                new BlogPost { Title = "New", Slug = "new", PackageId = "requests", Version = "2.32.0", Registry = "pypi", Content = "short", IsPublished = true, CreatedAt = Now.AddDays(-1) },
                new BlogPost { Title = "Draft", Slug = "draft", PackageId = "requests", Version = "2.31.0", Registry = "pypi", Content = "draft", IsPublished = false, CreatedAt = Now });
            await context.SaveChangesAsync();
        }

        using var unitOfWork = database.CreateUnitOfWork();

        var jobs = new RecordingBackgroundJobClient();
        await new PackageAnalysisJob(unitOfWork, NullLogger<PackageAnalysisJob>.Instance, new HangfireJobScheduler(jobs)).ExecuteAsync();
        var requested = jobs.Jobs.Select(job => (AnalyzeRequest)job.Args[0]).Select(r => $"{r.Registry}/{r.PackageId}").Order();
        Assert.Equal(new[] { "npm/left-pad", "pypi/requests" }, requested);

        var recent = await unitOfWork.CachedPackages.GetTopDescendingWithIncludeAsync(p => p.CachedAt, p => p.Registry, 1);
        Assert.Equal("pypi", Assert.Single(recent).Registry.Value);

        var published = Assert.IsType<OkObjectResult>(await new BlogController(unitOfWork, new ReleaseNotesRenderer()).GetPublishedBlogs(page: 1, pageSize: 10));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(published.Value));
        Assert.Equal(2, body.RootElement.GetProperty("Total").GetInt32());
        var first = body.RootElement.GetProperty("Data")[0];
        var second = body.RootElement.GetProperty("Data")[1];
        Assert.Equal(("new", "short"), (first.GetProperty("Slug").GetString(), first.GetProperty("Excerpt").GetString()));
        Assert.Equal(203, second.GetProperty("Excerpt").GetString()!.Length);
    }

    [Fact]
    public async Task A_read_ends_when_its_work_is_cancelled_and_a_write_still_completes()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        using var unitOfWork = database.CreateUnitOfWork();
        using var work = new CancellationTokenSource();
        work.Cancel();

        using (WorkCancellation.Use(work.Token))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unitOfWork.Registries.GetAllAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => unitOfWork.ToListAsync(unitOfWork.Registries.Query().Select(r => r.Value)));

            await unitOfWork.Registries.AddAsync(new Registry { Value = "npm", Label = "npm" });
            Assert.Equal(1, await unitOfWork.SaveChangesAsync());
        }

        Assert.Equal(1, await unitOfWork.Registries.CountAsync());
    }
}
