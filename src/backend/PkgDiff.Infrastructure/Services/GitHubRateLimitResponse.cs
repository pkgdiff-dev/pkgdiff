using System.Net;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

public static class GitHubRateLimitResponse
{
    // A rate-limit refusal says nothing about the repository: not "no releases" or "no milestones"
    public static void ThrowIfLimited(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return;

        if (!response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues)
            || remainingValues.FirstOrDefault() != "0")
            return;

        var resetAt = response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var resetUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(resetUnix).UtcDateTime
                : DateTime.UtcNow.AddHours(1);

        throw new GitHubRateLimitedException(resetAt);
    }
}
