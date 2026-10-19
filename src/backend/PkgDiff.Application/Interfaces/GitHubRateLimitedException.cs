namespace PkgDiff.Application.Interfaces;

// The GitHub API budget is spent until ResetAt. Work that needs GitHub is to be tried again then,
// not recorded as finding nothing.
public class GitHubRateLimitedException : Exception
{
    public GitHubRateLimitedException(DateTime resetAt)
        : base($"GitHub API rate limit exhausted until {resetAt:u}")
    {
        ResetAt = resetAt;
    }

    public DateTime ResetAt { get; }
}
