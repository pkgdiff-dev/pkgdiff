namespace PkgDiff.Shared.DTOs;

public enum SocialPostOutcome
{
    // The platform confirmed the post (2xx)
    Succeeded,

    // Definitely not posted: the request was never sent, or the platform answered with a 4xx
    Rejected,

    // The request may have reached the platform but its fate is unknown (timeout, reset, 5xx);
    // retrying could publish a duplicate
    Unknown
}

public sealed record SocialPostResult(SocialPostOutcome Outcome, string? PostUrl, string? ErrorMessage)
{
    public bool Success => Outcome == SocialPostOutcome.Succeeded;

    public static SocialPostResult Succeeded(string? postUrl) => new(SocialPostOutcome.Succeeded, postUrl, null);

    public static SocialPostResult Rejected(string errorMessage) => new(SocialPostOutcome.Rejected, null, errorMessage);

    public static SocialPostResult Unknown(string errorMessage) => new(SocialPostOutcome.Unknown, null, errorMessage);
}
