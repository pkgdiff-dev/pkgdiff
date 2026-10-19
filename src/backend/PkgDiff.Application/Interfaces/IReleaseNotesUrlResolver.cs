namespace PkgDiff.Application.Interfaces;

/// <summary>
/// Detects URLs in release notes text that point to actual release notes content,
/// fetches and returns the resolved content. Returns original text if no URL resolution needed.
/// </summary>
public interface IReleaseNotesUrlResolver
{
    /// <summary>
    /// Inspects release notes text. If the text is URL-dominated (contains URLs but little
    /// substantive content), extracts URLs, fetches their content, and returns the resolved
    /// release notes. Otherwise returns the original text unchanged.
    /// </summary>
    Task<UrlResolverResult> ResolveAsync(
        string rawReleaseNotes,
        string packageId,
        string version,
        string? githubRepoUrl);
}

/// <param name="ReleaseNotes">The resolved (or original) release notes text</param>
/// <param name="WasResolved">True if URLs were detected and content was fetched from them</param>
/// <param name="ResolvedSource">Source description if resolved</param>
/// <param name="DiscoveredGitHubUrl">GitHub repo URL discovered during resolution, if any</param>
public record UrlResolverResult(
    string ReleaseNotes,
    bool WasResolved,
    string? ResolvedSource = null,
    string? DiscoveredGitHubUrl = null);
