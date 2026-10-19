using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Persistence;

public class BulkDeleteTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Clearing_resolved_scrape_logs_keeps_the_open_ones()
    {
        await _unitOfWork.ScrapeLogs.AddAsync(new ScrapeLog { PackageId = "a", IsResolved = true });
        await _unitOfWork.ScrapeLogs.AddAsync(new ScrapeLog { PackageId = "b", IsResolved = false });
        await _unitOfWork.ScrapeLogs.AddAsync(new ScrapeLog { PackageId = "c", IsResolved = true });

        await new AdminScrapeLogService(_unitOfWork).ClearResolvedScrapeLogsAsync();

        Assert.Equal(new[] { "b" }, _unitOfWork.ScrapeLogs.Items.Select(log => log.PackageId));
    }

    [Fact]
    public async Task Clearing_the_search_history_removes_every_row()
    {
        await _unitOfWork.SearchHistories.AddAsync(new SearchHistory { PackageId = "a" });
        await _unitOfWork.SearchHistories.AddAsync(new SearchHistory { PackageId = "b", IpAddress = "System" });

        await new AdminDashboardService(_unitOfWork).ClearSearchHistoryAsync();

        Assert.Empty(_unitOfWork.SearchHistories.Items);
    }

    [Fact]
    public async Task Deleting_a_package_removes_its_notes_and_leaves_other_packages_alone()
    {
        var removed = await _unitOfWork.CachedPackages.AddAsync(new CachedPackage { PackageId = "left-pad" });
        var kept = await _unitOfWork.CachedPackages.AddAsync(new CachedPackage { PackageId = "right-pad" });
        await _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote { CachedPackageId = removed.Id, Version = "1.0.0" });
        await _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote { CachedPackageId = removed.Id, Version = "1.1.0" });
        await _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote { CachedPackageId = kept.Id, Version = "2.0.0" });

        var packages = new AdminPackageService(_unitOfWork);
        await packages.DeletePackageAsync(removed.Id);

        Assert.Equal(new[] { "right-pad" }, _unitOfWork.CachedPackages.Items.Select(p => p.PackageId));
        Assert.Equal(new[] { "2.0.0" }, _unitOfWork.CachedReleaseNotes.Items.Select(n => n.Version));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => packages.DeletePackageAsync(removed.Id));
    }
}
