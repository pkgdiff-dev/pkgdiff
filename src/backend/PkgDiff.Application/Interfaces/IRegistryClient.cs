using PkgDiff.Application.Versioning;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

// Everything that differs from one registry to the next. Each registry has exactly one client,
// found by name through IRegistryServiceFactory.
public interface IRegistryClient
{
    // One of RegistryNames
    string Id { get; }

    // The release-notes source recorded for a version whose notes come from the registry itself
    string SourceLabel { get; }

    VersionScheme VersionScheme { get; }

    // Whether the range "from A to B" lists A itself
    bool RangeIncludesStartingVersion { get; }

    // False when the notes of a package can only come from its repository
    bool PublishesReleaseNotes { get; }

    // True when the metadata of a package names its folder in the repository (PackageMetadata.SourceDirectory)
    bool KnowsSourceDirectory => false;

    Task<bool> PackageExistsAsync(string packageId, bool includePrerelease);

    // Ascending in the registry's own version order, without the versions the exclude patterns remove
    Task<IReadOnlyList<PackageVersion>> GetVersionsAsync(string packageId, bool includePrerelease);

    Task<DateTime?> GetPublishedDateAsync(string packageId, string version);

    Task<PackageMetadata> GetMetadataAsync(string packageId);

    // The notes the registry itself publishes for a version; null when it publishes none
    Task<string?> GetOwnReleaseNotesAsync(string packageId, string version);

    IReleaseNotesScrapingStrategy GetScrapingStrategy(string? githubRepoUrl);
}
