using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class PubDevRegistryClient : ListedVersionsRegistryClient
{
    private readonly IPubDevService _pubDevService;
    private readonly PubDevScrapingStrategy _pubDevStrategy;

    public PubDevRegistryClient(
        IPubDevService pubDevService,
        PubDevScrapingStrategy pubDevStrategy,
        IVersionFilterService versionFilterService,
        ILogger<PubDevRegistryClient> logger)
        : base(versionFilterService, logger)
    {
        _pubDevService = pubDevService;
        _pubDevStrategy = pubDevStrategy;
    }

    public override string Id => RegistryNames.PubDev;

    public override string SourceLabel => "PubDev";

    public override VersionScheme VersionScheme => VersionScheme.SemVer;

    public override bool PublishesReleaseNotes => true;

    protected override async Task<IEnumerable<string>> ListVersionsAsync(string packageId, bool includePrerelease) =>
        (await _pubDevService.GetAllVersionsAsync(packageId)).Select(v => v.Version);

    public override async Task<DateTime?> GetPublishedDateAsync(string packageId, string version)
    {
        var versions = await _pubDevService.GetAllVersionsAsync(packageId);
        return versions.FirstOrDefault(v => v.Version == version).PublishDate;
    }

    protected override async Task<PackageMetadata> FetchMetadataAsync(string packageId)
    {
        var extraInfo = await _pubDevService.GetPackageExtraInfoAsync(packageId);
        var versions = await _pubDevService.GetAllVersionsAsync(packageId);
        if (versions.Count == 0)
            throw new ArgumentException($"Package '{packageId}' not found in {Id} registry.");

        return new PackageMetadata
        {
            PackageId = packageId,
            Registry = Id,
            // pub.dev lists the newest version first
            LatestVersion = versions[0].Version ?? string.Empty,
            Description = extraInfo.Description,
            Authors = extraInfo.Publisher,
            ProjectUrl = extraInfo.RepositoryUrl,
            GitHubRepositoryUrl = extraInfo.RepositoryUrl,
            Versions = versions
                .Select(v => new VersionSummary { Version = v.Version, PublishedDate = v.PublishDate })
                .ToList()
        };
    }

    public override async Task<string?> GetOwnReleaseNotesAsync(string packageId, string version) =>
        await _pubDevService.GetReleaseNotesAsync(packageId, version);

    public override IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl) => _pubDevStrategy;
}
