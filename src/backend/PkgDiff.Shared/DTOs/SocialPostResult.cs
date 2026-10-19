namespace PkgDiff.Shared.DTOs;

public enum SocialPostOutcome
{
    Succeeded,

    // Definitely not posted: never sent, or the platform answered with a 4xx
    Rejected,

    // May have reached the platform (timeout, reset, 5xx); retrying could publish a duplicate
    Unknown
}

public sealed record SocialPostResult(SocialPostOutcome Outcome, string? PostUrl, string? ErrorMessage)
{
    public bool Success => Outcome == SocialPostOutcome.Succeeded;

    public static SocialPostResult Succeeded(string? postUrl) => new(SocialPostOutcome.Succeeded, postUrl, null);

    public static SocialPostResult Rejected(string errorMessage) => new(SocialPostOutcome.Rejected, null, errorMessage);

    public static SocialPostResult Unknown(string errorMessage) => new(SocialPostOutcome.Unknown, null, errorMessage);
}
