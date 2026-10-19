using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Registries;

public class RegistryClientTests
{
    [Theory]
    [InlineData("nuget", "nuget", "NuGet", VersionScheme.NuGet, false, true)]
    [InlineData("NuGet", "nuget", "NuGet", VersionScheme.NuGet, false, true)]
    [InlineData("npm", "npm", "NPM", VersionScheme.SemVer, true, false)]
    [InlineData("pypi", "pypi", "PyPI", VersionScheme.Pep440, true, false)]
    [InlineData("PyPI", "pypi", "PyPI", VersionScheme.Pep440, true, false)]
    [InlineData("pub", "pub.dev", "PubDev", VersionScheme.SemVer, true, true)]
    [InlineData("pub.dev", "pub.dev", "PubDev", VersionScheme.SemVer, true, true)]
    public void Every_spelling_of_a_registry_reaches_its_one_client(
        string spelling, string id, string label, VersionScheme scheme, bool includesStart, bool publishesNotes)
    {
        var client = TestRegistries.Create().GetClient(spelling);

        Assert.Equal(id, client.Id);
        Assert.Equal(label, client.SourceLabel);
        Assert.Equal(scheme, client.VersionScheme);
        Assert.Equal(includesStart, client.RangeIncludesStartingVersion);
        Assert.Equal(publishesNotes, client.PublishesReleaseNotes);
    }

    [Theory]
    [InlineData("maven")]
    [InlineData("")]
    [InlineData("nuget ")]
    public void A_registry_without_a_client_is_a_caller_fault_not_a_nuget_lookup(string registry)
    {
        var factory = TestRegistries.Create();

        Assert.False(factory.TryGetClient(registry, out _));
        Assert.False(factory.TryGetClient(null, out _));
        Assert.Throws<ArgumentException>(() => factory.GetClient(registry));
    }

    [Fact]
    public void Two_clients_for_one_registry_fail_when_the_factory_is_built()
    {
        var one = TestRegistries.Create().GetClient("npm");
        var other = TestRegistries.Create().GetClient("npm");

        Assert.Throws<ArgumentException>(() => new RegistryServiceFactory([one, other]));
    }

    [Fact]
    public void Every_registry_name_has_a_client_and_a_package_id_grammar()
    {
        var factory = TestRegistries.Create();

        Assert.All(RegistryNames.All, name =>
        {
            Assert.Equal(name, factory.GetClient(name).Id);
            Assert.Equal(name, RegistryNames.Canonical(name.ToUpperInvariant()));
            Assert.True(PkgDiff.Shared.Validation.PackageIdRules.IsValidPackageId(name, "abc"));
        });
        Assert.Equal(new[] { "pub.dev", "pub" }, RegistryNames.SpellingsOf("PUB"));
        Assert.Equal(new[] { "npm" }, RegistryNames.SpellingsOf("npm"));
        Assert.Equal(new[] { "maven" }, RegistryNames.SpellingsOf("Maven"));
        Assert.Null(RegistryNames.Canonical("maven"));
    }

    [Fact]
    public void NuGet_asks_its_package_page_first_until_a_repository_is_known()
    {
        var (githubFirst, nugetFirst, pubDev) = CreateStrategies();
        var factory = TestRegistries.Create(githubFirst: githubFirst, nugetFirst: nugetFirst, pubDevStrategy: pubDev);

        Assert.Same(nugetFirst, factory.GetClient("nuget").GetScrapingStrategy(null));
        Assert.Same(nugetFirst, factory.GetClient("nuget").GetScrapingStrategy(" "));
        Assert.Same(githubFirst, factory.GetClient("nuget").GetScrapingStrategy("https://github.com/o/r"));
        Assert.Same(githubFirst, factory.GetClient("npm").GetScrapingStrategy(null));
        Assert.Same(githubFirst, factory.GetClient("pypi").GetScrapingStrategy(null));
        Assert.Same(pubDev, factory.GetClient("pub").GetScrapingStrategy("https://github.com/o/r"));
    }

    [Fact]
    public async Task Pub_dev_metadata_carries_the_versions_and_the_repository()
    {
        var pubDev = new StubPubDev(
            [("2.0.0", new DateTime(2026, 5, 1)), ("1.0.0", new DateTime(2025, 5, 1))],
            ("publisher.dev", "A package", "https://github.com/o/r"));
        var client = TestRegistries.Create(pubDev: pubDev).GetClient("pub");

        var metadata = await client.GetMetadataAsync("provider");

        Assert.Equal("2.0.0", metadata.LatestVersion);
        Assert.Equal("publisher.dev", metadata.Authors);
        Assert.Equal("https://github.com/o/r", metadata.GitHubRepositoryUrl);
        Assert.Equal(new[] { "2.0.0", "1.0.0" }, metadata.Versions.Select(v => v.Version));
        Assert.Equal(new DateTime(2025, 5, 1), await client.GetPublishedDateAsync("provider", "1.0.0"));
        Assert.Null(await client.GetPublishedDateAsync("provider", "9.9.9"));
        Assert.Equal("notes of 2.0.0", await client.GetOwnReleaseNotesAsync("provider", "2.0.0"));
    }

    [Fact]
    public async Task A_pub_dev_package_without_versions_does_not_exist()
    {
        var client = TestRegistries.Create(
            pubDev: new StubPubDev([], (null, null, null)),
            versionFilter: new VersionFilterService(
                new FakeUnitOfWork(), new MemoryCache(new MemoryCacheOptions()), NullLogger<VersionFilterService>.Instance))
            .GetClient("pub.dev");

        Assert.False(await client.PackageExistsAsync("nothing", includePrerelease: false));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMetadataAsync("nothing"));
    }

    [Fact]
    public async Task GitHub_first_finds_the_repository_through_the_registry_of_the_package()
    {
        var github = new StubGitHub(new() { ["1.2.0"] = "## What changed" });
        var logs = new RecordingScrapeLogs();
        var pypi = new StubPyPI(new PackageMetadata { PackageId = "requests", GitHubRepositoryUrl = "https://github.com/psf/requests" });
        var strategy = new GitHubFirstScrapingStrategy(github, new PassThroughResolver(), logs, NullLogger<GitHubFirstScrapingStrategy>.Instance);
        var registry = TestRegistries.Create(pypi: pypi, githubFirst: strategy).GetClient("pypi");

        var found = await strategy.GetReleaseNotesAsync("requests", "1.2.0", null, registry);
        var missing = await strategy.GetReleaseNotesAsync("requests", "9.9.9", null, registry);

        Assert.Equal(new ReleaseNotesResult("## What changed", "GitHub", "https://github.com/psf/requests"), found);
        Assert.Equal(string.Empty, missing.ReleaseNotes);

        // PyPI publishes no notes of its own, so GitHub was the only source tried
        var failure = Assert.Single(logs.Failures);
        Assert.Equal(("requests", "9.9.9", "pypi", "GitHub"), (failure.PackageId, failure.Version, failure.Registry, failure.AttemptedSources));
        Assert.Equal("1.2.0", failure.AvailableVersions);
    }

    [Fact]
    public async Task GitHub_first_falls_back_to_the_notes_the_registry_publishes()
    {
        var logs = new RecordingScrapeLogs();
        var strategy = new GitHubFirstScrapingStrategy(
            new StubGitHub(new()), new PassThroughResolver(), logs, NullLogger<GitHubFirstScrapingStrategy>.Instance);
        var nuget = TestRegistries.Create(nugetPage: new StubNuGetPage("Fixed a crash."), githubFirst: strategy).GetClient("nuget");
        var silentNuget = TestRegistries.Create(nugetPage: new StubNuGetPage(string.Empty), githubFirst: strategy).GetClient("nuget");

        var fromPage = await strategy.GetReleaseNotesAsync("My.Pkg", "1.0.0", "https://github.com/o/r", nuget);
        var nothing = await strategy.GetReleaseNotesAsync("My.Pkg", "1.0.0", "https://github.com/o/r", silentNuget);

        Assert.Equal(new ReleaseNotesResult("Fixed a crash.", "NuGet", null), fromPage);
        Assert.Equal(new ReleaseNotesResult(string.Empty, "GitHub", null), nothing);
        Assert.Equal("GitHub, NuGet", Assert.Single(logs.Failures).AttemptedSources);
    }

    [Fact]
    public async Task Cached_metadata_marks_pre_releases_by_the_scheme_of_the_registry()
    {
        var unitOfWork = new FakeUnitOfWork();
        var pypi = new Registry { Value = "pypi" };
        await unitOfWork.Registries.AddAsync(pypi);
        await unitOfWork.CachedPackages.AddAsync(new CachedPackage
        {
            PackageId = "requests",
            RegistryId = pypi.Id,
            Registry = pypi,
            LatestVersion = "2.0",
            GitHubRepositoryUrl = "https://github.com/psf/requests",
            AllVersionsJson = """["2.0","2.0rc1","1.0.post1","1.0.dev3"]""",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        var cache = new PackageCacheService(
            unitOfWork, TestRegistries.Create(), new ReleaseNotesRenderer(), null!,
            new CachedLookups(unitOfWork, new MemoryCache(new MemoryCacheOptions())), null!, NullLogger<PackageCacheService>.Instance);

        var metadata = await cache.GetOrFetchMetadataAsync("pypi", "requests");

        Assert.Equal(
            new[] { ("2.0", false), ("2.0rc1", true), ("1.0.post1", false), ("1.0.dev3", true) },
            metadata.Versions.Select(v => (v.Version, v.IsPrerelease)));
    }

    // Under a Turkish culture "PYPI".ToLower() is "pypı", which is no registry
    [Fact]
    public async Task A_registry_name_is_matched_the_same_way_under_every_culture()
    {
        CultureInfo turkish;
        try
        {
            turkish = CultureInfo.GetCultureInfo("tr-TR");
        }
        catch (CultureNotFoundException)
        {
            turkish = CultureInfo.InvariantCulture;
        }
        Assert.SkipUnless("I".ToLower(turkish) == "ı", "this runtime has no culture data");

        var unitOfWork = new FakeUnitOfWork();
        var pypi = new Registry { Value = "pypi" };
        await unitOfWork.Registries.AddAsync(pypi);
        await unitOfWork.CachedPackages.AddAsync(new CachedPackage
        {
            PackageId = "requests",
            RegistryId = pypi.Id,
            Registry = pypi,
            LatestVersion = "2.0",
            GitHubRepositoryUrl = "https://github.com/psf/requests",
            AllVersionsJson = """["2.0"]""",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        var cache = new PackageCacheService(
            unitOfWork, TestRegistries.Create(), new ReleaseNotesRenderer(), null!,
            new CachedLookups(unitOfWork, new MemoryCache(new MemoryCacheOptions())), null!, NullLogger<PackageCacheService>.Instance);

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = turkish;
        try
        {
            var metadata = await cache.GetOrFetchMetadataAsync("PYPI", "requests");

            Assert.Equal("pypi", metadata.Registry);
            Assert.Equal("2.0", metadata.LatestVersion);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static (GitHubFirstScrapingStrategy, NuGetFirstScrapingStrategy, PubDevScrapingStrategy) CreateStrategies() =>
        (new GitHubFirstScrapingStrategy(null!, null!, null!, NullLogger<GitHubFirstScrapingStrategy>.Instance),
         new NuGetFirstScrapingStrategy(null!, null!, null!, null!, NullLogger<NuGetFirstScrapingStrategy>.Instance),
         new PubDevScrapingStrategy(null!, NullLogger<PubDevScrapingStrategy>.Instance));

    private sealed class StubGitHub(Dictionary<string, string> releases) : IGitHubScraperService
    {
        public Task<string?> ExtractGitHubRepositoryUrlAsync(string packageId) => throw new NotSupportedException();

        public Task<Dictionary<string, string>> GetAllReleasesAsync(string githubRepoUrl) => Task.FromResult(releases);

        public Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version) =>
            Task.FromResult(releases.GetValueOrDefault(version));
    }

    private sealed class PassThroughResolver : IReleaseNotesUrlResolver
    {
        public Task<UrlResolverResult> ResolveAsync(string rawReleaseNotes, string packageId, string version, string? githubRepoUrl) =>
            Task.FromResult(new UrlResolverResult(rawReleaseNotes, WasResolved: false));
    }

    private sealed class RecordingScrapeLogs : IScrapeLogService
    {
        public List<(string PackageId, string Version, string Registry, string? AvailableVersions, string AttemptedSources)> Failures { get; } = [];

        public Task LogFailureAsync(
            string packageId, string version, string registry, string? githubRepoUrl,
            string failureReason, string? availableVersions, string attemptedSources)
        {
            Failures.Add((packageId, version, registry, availableVersions, attemptedSources));
            return Task.CompletedTask;
        }
    }

    private sealed class StubNuGetPage(string notes) : IScraperService
    {
        public Task<string> GetReleaseNotesAsync(string packageId, string version) => Task.FromResult(notes);

        public Task<string?> GetGitHubRepositoryUrlAsync(string packageId) => throw new NotSupportedException();
    }

    private sealed class StubPyPI(PackageMetadata metadata) : IPyPIService
    {
        public Task<PackageMetadata> GetPackageMetadataAsync(string packageId) => Task.FromResult(metadata);

        public Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false) =>
            throw new NotSupportedException();

        public Task<IEnumerable<string>> GetVersionsInRangeAsync(
            string packageId, string fromVersion, string toVersion, bool includePrerelease = false) =>
            throw new NotSupportedException();

        public Task<bool> PackageExistsAsync(string packageId) => throw new NotSupportedException();

        public Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version) =>
            throw new NotSupportedException();
    }

    private sealed class StubPubDev(
        List<(string Version, DateTime? PublishDate)> versions,
        (string? Publisher, string? Description, string? RepositoryUrl) extraInfo) : IPubDevService
    {
        public Task<List<(string Version, DateTime? PublishDate)>> GetAllVersionsAsync(string packageId) => Task.FromResult(versions);

        public Task<(string? Publisher, string? Description, string? RepositoryUrl)> GetPackageExtraInfoAsync(string packageId) =>
            Task.FromResult(extraInfo);

        public Task<string> GetReleaseNotesAsync(string packageId, string version) => Task.FromResult($"notes of {version}");

        public Task<string?> GetGitHubRepositoryUrlAsync(string packageId) => throw new NotSupportedException();
    }
}
