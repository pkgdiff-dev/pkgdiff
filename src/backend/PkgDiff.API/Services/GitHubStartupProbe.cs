using System.Net;
using System.Text.Json;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.API.Services;

public static class GitHubStartupProbe
{
    public const string RateLimitUrl = "https://api.github.com/rate_limit";

    private const int UnauthenticatedHourlyLimit = 60;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    // Never log any part of the token: even fragments identify it in leaked logs
    public static async Task LogStatusAsync(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var githubSettings = configuration.GetSection("GitHubSettings");
        var personalAccessToken = githubSettings.GetValue<string?>("PersonalAccessToken");
        var enableAuthentication = githubSettings.GetValue("EnableAuthentication", true);
        var useGitHubApi = githubSettings.GetValue("UseGitHubApi", true);
        var userAgent = githubSettings.GetValue<string?>("UserAgent") ?? "PkgDiff/1.0";
        var tokenConfigured = !string.IsNullOrWhiteSpace(personalAccessToken);

        logger.LogInformation(
            "GitHub API configuration: UserAgent {UserAgent}, AuthenticationEnabled {AuthenticationEnabled}, UseGitHubApi {UseGitHubApi}, PersonalAccessTokenConfigured {TokenConfigured}",
            userAgent, enableAuthentication, useGitHubApi, tokenConfigured);

        if (!tokenConfigured)
        {
            logger.LogWarning("No GitHub personal access token configured; the GitHub API uses the unauthenticated rate limit (60 requests/hour)");
            return;
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientNames.GitHubApi);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, RateLimitUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (enableAuthentication)
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"token {personalAccessToken}");
            }

            using var response = await httpClient.SendAsync(request, timeout.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("GitHub rejected the configured personal access token (401); it is invalid, expired or revoked");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GitHub rate limit probe returned status {StatusCode}", (int)response.StatusCode);
                return;
            }

            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("rate", out var rate)
                || !rate.TryGetProperty("limit", out var limitElement)
                || !rate.TryGetProperty("remaining", out var remainingElement)
                || !rate.TryGetProperty("reset", out var resetElement))
            {
                logger.LogWarning("GitHub rate limit probe returned an unexpected response body");
                return;
            }

            var limit = limitElement.GetInt32();
            var remaining = remainingElement.GetInt32();
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetElement.GetInt64());

            logger.LogInformation(
                "GitHub API reachable: {Remaining}/{Limit} requests remaining, resets at {ResetTime:u}",
                remaining, limit, resetTime);

            if (enableAuthentication && limit <= UnauthenticatedHourlyLimit)
            {
                logger.LogWarning("GitHub reports the unauthenticated rate limit although a personal access token is configured; the token is not being accepted");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Could not complete the GitHub rate limit probe at startup ({ErrorType}); non-fatal, scraping still works at runtime",
                ex.GetType().Name);
        }
    }
}
