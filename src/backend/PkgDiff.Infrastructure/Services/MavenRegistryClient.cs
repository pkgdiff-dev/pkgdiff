using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public partial class MavenRegistryClient : ListedVersionsRegistryClient
{
    private readonly IMavenService _mavenService;
    private readonly GitHubFirstScrapingStrategy _githubFirstStrategy;

    public MavenRegistryClient(
        IMavenService mavenService,
        GitHubFirstScrapingStrategy githubFirstStrategy,
        IVersionFilterService versionFilterService,
        ILogger<MavenRegistryClient> logger)
        : base(versionFilterService, logger)
    {
        _mavenService = mavenService;
        _githubFirstStrategy = githubFirstStrategy;
    }

    public override string Id => RegistryNames.Maven;

    public override string SourceLabel => "Maven Central";

    public override VersionScheme VersionScheme => VersionScheme.Maven;

    public override bool PublishesReleaseNotes => false;

    protected override Task<IEnumerable<string>> ListVersionsAsync(string packageId, bool includePrerelease) =>
        _mavenService.GetPackageVersionsAsync(packageId, includePrerelease);

    public override Task<DateTime?> GetPublishedDateAsync(string packageId, string version) =>
        _mavenService.GetPackagePublishedDateAsync(packageId, version);

    protected override Task<PackageMetadata> FetchMetadataAsync(string packageId) =>
        _mavenService.GetPackageMetadataAsync(packageId);

    public override Task<string?> GetOwnReleaseNotesAsync(string packageId, string version) =>
        Task.FromResult<string?>(null);

    public override IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl) => _githubFirstStrategy;

    // 6.4.0.Final and 5.3.0.RELEASE are releases; the "final" and "release" patterns must not hide them
    protected override string FilteredSpelling(string version) => ReleaseSuffix().Replace(version, string.Empty);

    [GeneratedRegex(@"[.-](?:final|release|ga)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseSuffix();
}
