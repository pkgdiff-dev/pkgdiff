namespace PkgDiff.Application.Interfaces;

public interface IGitHubScraperService
{
    Task<string?> ExtractGitHubRepositoryUrlAsync(string packageId);

    Task<Dictionary<string, string>> GetAllReleasesAsync(string githubRepoUrl);

    Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version);

    // The package name tells a monorepo's per-package tags apart
    Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version, string? packageId) =>
        GetReleaseNotesForVersionAsync(githubRepoUrl, version);
}
