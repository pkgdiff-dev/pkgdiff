using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class NuGetRegistryClient : IRegistryClient
{
    private readonly INugetService _nugetService;
    private readonly IScraperService _scraperService;
    private readonly GitHubFirstScrapingStrategy _githubFirstStrategy;
    private readonly NuGetFirstScrapingStrategy _nugetFirstStrategy;

    public NuGetRegistryClient(
        INugetService nugetService,
        IScraperService scraperService,
        GitHubFirstScrapingStrategy githubFirstStrategy,
        NuGetFirstScrapingStrategy nugetFirstStrategy)
    {
        _nugetService = nugetService;
        _scraperService = scraperService;
        _githubFirstStrategy = githubFirstStrategy;
        _nugetFirstStrategy = nugetFirstStrategy;
    }

    public string Id => RegistryNames.NuGet;

    public string SourceLabel => "NuGet";

    public VersionScheme VersionScheme => VersionScheme.NuGet;

    // NuGet has always left the starting version out of the range
    public bool RangeIncludesStartingVersion => false;

    public bool PublishesReleaseNotes => true;

    public Task<bool> PackageExistsAsync(string packageId, bool includePrerelease) =>
        _nugetService.PackageExistsAsync(packageId);

    // The NuGet service lists parsed versions and applies the exclude patterns itself
    public async Task<IReadOnlyList<PackageVersion>> GetVersionsAsync(string packageId, bool includePrerelease) =>
        (await _nugetService.GetPackageVersionsAsync(packageId, includePrerelease))
            .Select(PackageVersion.FromNuGet)
            .OrderBy(v => v)
            .ToList();

    public Task<DateTime?> GetPublishedDateAsync(string packageId, string version) =>
        _nugetService.GetPackagePublishedDateAsync(packageId, version);

    public Task<PackageMetadata> GetMetadataAsync(string packageId) =>
        _nugetService.GetPackageMetadataAsync(packageId);

    public async Task<string?> GetOwnReleaseNotesAsync(string packageId, string version) =>
        await _scraperService.GetReleaseNotesAsync(packageId, version);

    // The package page is asked first until a repository is known
    public IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl) =>
        string.IsNullOrWhiteSpace(githubRepoUrl) ? _nugetFirstStrategy : _githubFirstStrategy;
}
