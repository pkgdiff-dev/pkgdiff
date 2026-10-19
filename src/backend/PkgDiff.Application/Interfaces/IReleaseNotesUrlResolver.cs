namespace PkgDiff.Application.Interfaces;

public interface IReleaseNotesUrlResolver
{
    // Fetches the linked content when the notes are mostly URLs; otherwise returns them unchanged
    Task<UrlResolverResult> ResolveAsync(
        string rawReleaseNotes,
        string packageId,
        string version,
        string? githubRepoUrl);
}

public record UrlResolverResult(
    string ReleaseNotes,
    bool WasResolved,
    string? ResolvedSource = null,
    string? DiscoveredGitHubUrl = null);
