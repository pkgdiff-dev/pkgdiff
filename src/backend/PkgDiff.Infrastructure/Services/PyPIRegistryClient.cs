using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class PyPIRegistryClient : ListedVersionsRegistryClient
{
    private readonly IPyPIService _pypiService;
    private readonly GitHubFirstScrapingStrategy _githubFirstStrategy;

    public PyPIRegistryClient(
        IPyPIService pypiService,
        GitHubFirstScrapingStrategy githubFirstStrategy,
        IVersionFilterService versionFilterService,
        ILogger<PyPIRegistryClient> logger)
        : base(versionFilterService, logger)
    {
        _pypiService = pypiService;
        _githubFirstStrategy = githubFirstStrategy;
    }

    public override string Id => RegistryNames.PyPI;

    public override string SourceLabel => "PyPI";

    public override VersionScheme VersionScheme => VersionScheme.Pep440;

    public override bool PublishesReleaseNotes => false;

    protected override Task<IEnumerable<string>> ListVersionsAsync(string packageId, bool includePrerelease) =>
        _pypiService.GetPackageVersionsAsync(packageId, includePrerelease);

    public override Task<DateTime?> GetPublishedDateAsync(string packageId, string version) =>
        _pypiService.GetPackagePublishedDateAsync(packageId, version);

    protected override Task<PackageMetadata> FetchMetadataAsync(string packageId) =>
        _pypiService.GetPackageMetadataAsync(packageId);

    public override Task<string?> GetOwnReleaseNotesAsync(string packageId, string version) =>
        Task.FromResult<string?>(null);

    public override IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl) => _githubFirstStrategy;
}
