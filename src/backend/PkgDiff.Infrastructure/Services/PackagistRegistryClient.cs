using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class PackagistRegistryClient : ListedVersionsRegistryClient
{
    private readonly IPackagistService _packagistService;
    private readonly GitHubFirstScrapingStrategy _githubFirstStrategy;

    public PackagistRegistryClient(
        IPackagistService packagistService,
        GitHubFirstScrapingStrategy githubFirstStrategy,
        IVersionFilterService versionFilterService,
        ILogger<PackagistRegistryClient> logger)
        : base(versionFilterService, logger)
    {
        _packagistService = packagistService;
        _githubFirstStrategy = githubFirstStrategy;
    }

    public override string Id => RegistryNames.Packagist;

    public override string SourceLabel => "Packagist";

    public override VersionScheme VersionScheme => VersionScheme.Composer;

    public override bool PublishesReleaseNotes => false;

    protected override Task<IEnumerable<string>> ListVersionsAsync(string packageId, bool includePrerelease) =>
        _packagistService.GetPackageVersionsAsync(packageId, includePrerelease);

    public override Task<DateTime?> GetPublishedDateAsync(string packageId, string version) =>
        _packagistService.GetPackagePublishedDateAsync(packageId, version);

    protected override Task<PackageMetadata> FetchMetadataAsync(string packageId) =>
        _packagistService.GetPackageMetadataAsync(packageId);

    public override Task<string?> GetOwnReleaseNotesAsync(string packageId, string version) =>
        Task.FromResult<string?>(null);

    public override IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl) => _githubFirstStrategy;
}
