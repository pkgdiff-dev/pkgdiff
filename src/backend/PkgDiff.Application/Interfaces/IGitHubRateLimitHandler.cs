namespace PkgDiff.Application.Interfaces;

public interface IGitHubRateLimitHandler
{
    // Throws GitHubRateLimitedException while the budget is spent until the reset
    Task<bool> CanMakeRequestAsync();

    Task UpdateFromHeadersAsync(HttpResponseMessage response);

    Task<(int Remaining, int Limit, DateTime? ResetTime)> GetRateLimitStatusAsync();
}
