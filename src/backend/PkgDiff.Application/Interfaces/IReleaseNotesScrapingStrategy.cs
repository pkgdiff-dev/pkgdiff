using PkgDiff.Domain.Entities;

namespace PkgDiff.Application.Interfaces;

public interface IReleaseNotesScrapingStrategy
{
    Task<ReleaseNotesResult> GetReleaseNotesAsync(string packageId, string version, string? githubRepoUrl, IRegistryClient registry);
}

public record ReleaseNotesResult(
    string ReleaseNotes,
    string Source,
    string? DiscoveredGitHubUrl = null);
