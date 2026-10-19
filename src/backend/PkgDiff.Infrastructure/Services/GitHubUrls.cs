using Microsoft.Extensions.Logging;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

public static class GitHubUrls
{
    public static string Clean(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        url = url.TrimEnd('/');

        // Remove common GitHub paths to get base repository URL
        var segments = new[] { "/releases", "/issues", "/wiki", "/pulls", "/actions", "/tree", "/blob", "/commit" };
        foreach (var segment in segments)
        {
            var index = url.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                url = url.Substring(0, index);
                break;
            }
        }

        return url;
    }
    
    // The folder a repository URL points into: "packages/core" for
    // https://github.com/o/r/tree/main/packages/core; null for the repository itself or a file
    public static string? SubPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(url.TrimEnd('/'), @"github\.com/[^/]+/[^/]+/(?:tree|blob)/[^/]+/(?<path>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var path = Uri.UnescapeDataString(match.Groups["path"].Value).Trim('/');
        // A link to a file (packages/core/README.md) points into the folder that holds it
        var lastSlash = path.LastIndexOf('/');
        if (System.Text.RegularExpressions.Regex.IsMatch(path[(lastSlash + 1)..], @"\.(md|markdown|rst|txt|adoc)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            path = lastSlash < 0 ? string.Empty : path[..lastSlash];
        return path.Length == 0 || path.Contains("..") ? null : path;
    }

    // The https URL of a repository as a package manifest names it: "github:o/r", the bare "o/r" npm
    // takes for GitHub, "git@github.com:o/r.git", "ssh://git@github.com/o/r", "git+https://github.com/o/r.git".
    // Anything else comes back without its git decorations.
    public static string? FromRepositoryField(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        var url = repository.Trim();
        var hash = url.IndexOf('#');
        if (hash >= 0)
            url = url[..hash];

        var shorthand = System.Text.RegularExpressions.Regex.Match(url, @"^(?:github:)?(?<path>[\w.-]+/[\w.-]+)$");
        if (shorthand.Success)
            return "https://github.com/" + TrimGitSuffix(shorthand.Groups["path"].Value);

        if (url.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            url = url[4..];
        url = System.Text.RegularExpressions.Regex.Replace(url, @"^(?:ssh://)?git@github\.com[:/]", "https://github.com/",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        url = System.Text.RegularExpressions.Regex.Replace(url, @"^(?:git|http)://", "https://",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return TrimGitSuffix(url.TrimEnd('/'));
    }

    // True for an address on github.com in any spelling a manifest uses; a host that merely contains
    // "github.com" (github.com.example.org) is not GitHub
    public static bool IsGitHub(string? url)
    {
        var normalized = FromRepositoryField(url);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
               && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase));
    }

    // Only the ending: "bar.github.io" keeps its ".git"
    private static string TrimGitSuffix(string url) =>
        url.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? url[..^4] : url;

    // Changelog paths may contain folders, so each part is escaped on its own and the separators are kept
    public static string EscapeFilePath(string filePath)
        => string.Join('/', filePath.Split('/').Select(PackageUrlEncoder.EscapeSegment));

    public static (string owner, string repo) Parse(string githubUrl, ILogger logger)
    {
        try
        {
            var cleanUrl = Clean(githubUrl);
            var uri = new Uri(cleanUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            
            if (segments.Length >= 2)
            {
                return (segments[0], segments[1]);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GitHubScraper] Failed to parse GitHub URL: {Url}", githubUrl);
        }
        
        return (string.Empty, string.Empty);
    }
}
