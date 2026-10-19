namespace PkgDiff.Application.Interfaces;

public interface IGitHubScraperService
{
    /// <summary>
    /// Extracts the GitHub repository URL from a NuGet package page
    /// </summary>
    Task<string?> ExtractGitHubRepositoryUrlAsync(string packageId);

    /// <summary>
    /// Gets all releases from a GitHub repository
    /// </summary>
    /// <returns>Dictionary with version as key and release notes as value</returns>
    Task<Dictionary<string, string>> GetAllReleasesAsync(string githubRepoUrl);

    /// <summary>
    /// Gets release notes for a specific version from GitHub Releases
    /// </summary>
    Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version);

    // With the package name a monorepo's tags ("socket.io@4.8.3", "@scope/core@1.2.0") can be told apart
    Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version, string? packageId) =>
        GetReleaseNotesForVersionAsync(githubRepoUrl, version);
}
