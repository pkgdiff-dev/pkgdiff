using PkgDiff.Application.Versioning;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IRegistryClient
{
    string Id { get; }

    // Source recorded for notes that come from the registry itself
    string SourceLabel { get; }

    VersionScheme VersionScheme { get; }

    // Whether the range "from A to B" lists A itself
    bool RangeIncludesStartingVersion { get; }

    // False when the notes of a package can only come from its repository
    bool PublishesReleaseNotes { get; }

    // True when package metadata names its folder in the repository
    bool KnowsSourceDirectory => false;

    Task<bool> PackageExistsAsync(string packageId, bool includePrerelease);

    // Ascending in the registry's own version order, without the versions the exclude patterns remove
    Task<IReadOnlyList<PackageVersion>> GetVersionsAsync(string packageId, bool includePrerelease);

    Task<DateTime?> GetPublishedDateAsync(string packageId, string version);

    Task<PackageMetadata> GetMetadataAsync(string packageId);

    Task<string?> GetOwnReleaseNotesAsync(string packageId, string version);

    IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl);
}
