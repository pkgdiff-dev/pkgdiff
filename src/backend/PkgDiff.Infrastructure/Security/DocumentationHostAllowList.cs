namespace PkgDiff.Infrastructure.Security;

public static class DocumentationHostAllowList
{
    // Suffix domains: the host must equal or end with ".<entry>" (exact label boundary, never substring).
    private static readonly string[] SuffixDomains =
    {
        "readthedocs.io",
        "readthedocs.org",
    };

    // Exact hosts: the host must equal one of these verbatim.
    private static readonly HashSet<string> ExactHosts = new(StringComparer.Ordinal)
    {
        "docs.rs",
        "pkg.go.dev",
        "developer.mozilla.org",
        "docs.python.org",
        "docs.oracle.com",
    };

    public static bool IsAllowed(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        host = host.ToLowerInvariant().TrimEnd('.');

        if (ExactHosts.Contains(host))
            return true;

        foreach (var domain in SuffixDomains)
        {
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
