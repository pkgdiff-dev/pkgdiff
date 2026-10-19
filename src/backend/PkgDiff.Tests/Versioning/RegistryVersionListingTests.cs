using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Versioning;

public class RegistryVersionListingTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RegistryServiceFactory CreateFactory(IEnumerable<string> listed)
    {
        var listing = new StubListing(listed);
        var filter = new VersionFilterService(
            _unitOfWork, new MemoryCache(new MemoryCacheOptions()), NullLogger<VersionFilterService>.Instance);

        return TestRegistries.Create(nuget: listing, npm: listing, pypi: listing, versionFilter: filter);
    }

    private static PackageVersion Parse(IRegistryClient registry, string value)
    {
        Assert.True(registry.TryParseVersion(value, out var version));
        return version!;
    }

    [Fact]
    public async Task PyPI_versions_with_pre_post_and_epoch_parts_are_listed_in_order_without_failing()
    {
        var pypi = CreateFactory(["2.0.post1", "1.0", "2.0", "1!0.1", "2.0rc1", "1.0.dev2", "weird version"]).GetClient("pypi");

        var all = await pypi.GetVersionsAsync("requests", includePrerelease: true);
        var stable = await pypi.GetVersionsAsync("requests", includePrerelease: false);

        Assert.Equal(new[] { "1.0.dev2", "1.0", "2.0rc1", "2.0", "2.0.post1", "1!0.1" }, all.Select(v => v.Value));
        Assert.Equal(new[] { "1.0", "2.0", "2.0.post1", "1!0.1" }, stable.Select(v => v.Value));
    }

    [Fact]
    public async Task A_PyPI_range_is_cut_by_version_order_and_returned_newest_first()
    {
        var pypi = CreateFactory(["1.0", "1.5", "2.0rc1", "2.0", "2.0.post1", "3.0"]).GetClient("pypi");

        var range = await pypi.GetVersionsInRangeAsync(
            "requests", Parse(pypi, "1.5"), Parse(pypi, "2.0.post1"), includePrerelease: true);

        Assert.Equal(new[] { "2.0.post1", "2.0", "2.0rc1", "1.5" }, range.Select(v => v.Value));
    }

    [Fact]
    public async Task Npm_versions_are_ordered_by_version_even_when_a_backport_was_published_last()
    {
        var npm = CreateFactory(["1.0.0", "2.0.0", "2.1.0", "1.0.1"]).GetClient("npm");

        var all = await npm.GetVersionsAsync("left-pad", includePrerelease: false);

        Assert.Equal("2.1.0", all[^1].Value);
        Assert.Equal("2.0.0", all[^2].Value);
    }

    [Fact]
    public async Task Exclude_patterns_apply_to_registries_other_than_nuget()
    {
        await _unitOfWork.VersionExcludePatterns.AddAsync(new VersionExcludePattern { Pattern = "nightly", IsActive = true });
        await _unitOfWork.VersionExcludePatterns.AddAsync(new VersionExcludePattern { Pattern = "canary", IsActive = false });
        var npm = CreateFactory(["1.0.0", "1.1.0-nightly.3", "1.1.0-canary.1", "1.1.0"]).GetClient("npm");

        var all = await npm.GetVersionsAsync("left-pad", includePrerelease: true);

        Assert.Equal(new[] { "1.0.0", "1.1.0-canary.1", "1.1.0" }, all.Select(v => v.Value));
    }

    [Theory]
    [InlineData("nuget", new[] { "2.0.0", "1.5.0" })]
    [InlineData("npm", new[] { "2.0.0", "1.5.0", "1.0.0" })]
    [InlineData("pypi", new[] { "2.0.0", "1.5.0", "1.0.0" })]
    public async Task The_starting_version_of_a_range_is_listed_by_every_registry_except_nuget(string registry, string[] expected)
    {
        var client = CreateFactory(["1.0.0", "1.5.0", "2.0.0", "3.0.0"]).GetClient(registry);

        var range = await client.GetVersionsInRangeAsync(
            "pkg", Parse(client, "1.0.0"), Parse(client, "2.0.0"), includePrerelease: false);

        Assert.Equal(expected, range.Select(v => v.Value));
    }

    private sealed class StubListing(IEnumerable<string> versions) : INpmService, IPyPIService, INugetService
    {
        public Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false) =>
            Task.FromResult(versions);

        Task<IEnumerable<NuGetVersion>> INugetService.GetPackageVersionsAsync(string packageId, bool includePrerelease) =>
            Task.FromResult(versions.Select(NuGetVersion.Parse));

        public Task<IEnumerable<string>> GetVersionsInRangeAsync(
            string packageId, string fromVersion, string toVersion, bool includePrerelease = false) =>
            throw new NotSupportedException();

        public Task<IEnumerable<NuGetVersion>> GetVersionsInRangeAsync(
            string packageId, NuGetVersion fromVersion, NuGetVersion toVersion, bool includePrerelease = false) =>
            throw new NotSupportedException();

        public Task<bool> PackageExistsAsync(string packageId) => throw new NotSupportedException();

        public Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version) =>
            throw new NotSupportedException();

        public Task<PackageMetadata> GetPackageMetadataAsync(string packageId) => throw new NotSupportedException();
    }
}
