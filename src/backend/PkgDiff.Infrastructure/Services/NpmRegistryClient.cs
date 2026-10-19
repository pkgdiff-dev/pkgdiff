using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class NpmRegistryClient : ListedVersionsRegistryClient
{
    private readonly INpmService _npmService;
    private readonly GitHubFirstScrapingStrategy _githubFirstStrategy;

    public NpmRegistryClient(
        INpmService npmService,
        GitHubFirstScrapingStrategy githubFirstStrategy,
        IVersionFilterService versionFilterService,
        ILogger<NpmRegistryClient> logger)
        : base(versionFilterService, logger)
    {
        _npmService = npmService;
        _githubFirstStrategy = githubFirstStrategy;
    }

    public override string Id => RegistryNames.Npm;

    public override string SourceLabel => "NPM";

    public override VersionScheme VersionScheme => VersionScheme.SemVer;

    public override bool PublishesReleaseNotes => false;

    public override bool KnowsSourceDirectory => true;

    protected override Task<IEnumerable<string>> ListVersionsAsync(string packageId, bool includePrerelease) =>
        _npmService.GetPackageVersionsAsync(packageId, includePrerelease);

    public override Task<DateTime?> GetPublishedDateAsync(string packageId, string version) =>
        _npmService.GetPackagePublishedDateAsync(packageId, version);

    protected override Task<PackageMetadata> FetchMetadataAsync(string packageId) =>
        _npmService.GetPackageMetadataAsync(packageId);

    public override Task<string?> GetOwnReleaseNotesAsync(string packageId, string version) =>
        Task.FromResult<string?>(null);

    public override IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl) => _githubFirstStrategy;
}
