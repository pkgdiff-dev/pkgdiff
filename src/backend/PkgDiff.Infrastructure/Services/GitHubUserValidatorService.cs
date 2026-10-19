using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PkgDiff.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

/// <summary>
/// Validates GitHub usernames by calling the GitHub API users endpoint.
/// Filters out Organizations (only real User accounts are valid contributors).
/// Uses the same authenticated HTTP client as GitHubScraperService.
/// </summary>
public class GitHubUserValidatorService
{
    private static readonly TimeSpan SharedLifetime = TimeSpan.FromHours(6);

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUserValidatorService> _logger;
    private readonly IRateLimiterService? _rateLimiter;
    private readonly IGitHubRateLimitHandler? _rateLimitHandler;
    private readonly IMemoryCache? _sharedCache;

    // In-process cache so repeated mentions of same username don't hit API twice
    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    public GitHubUserValidatorService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GitHubUserValidatorService> logger,
        IRateLimiterService? rateLimiter = null,
        IGitHubRateLimitHandler? rateLimitHandler = null,
        IMemoryCache? sharedCache = null)
    {
        _logger = logger;
        _rateLimiter = rateLimiter;
        _rateLimitHandler = rateLimitHandler;
        _sharedCache = sharedCache;

        var githubSettings = configuration.GetSection("GitHubSettings");
        var token = githubSettings.GetValue<string?>("PersonalAccessToken");
        var userAgent = githubSettings.GetValue<string>("UserAgent", "PkgDiff/1.0");
        var enableAuth = githubSettings.GetValue<bool>("EnableAuthentication", true);

        _httpClient = httpClientFactory.CreateClient(HttpClientNames.GitHubApi);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        if (enableAuth && !string.IsNullOrWhiteSpace(token))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");
    }

    /// <summary>
    /// Returns true only if the username is a real GitHub <b>User</b> (not an Organization or Bot).
    /// Results are cached in-memory for the lifetime of the scoped service.
    /// </summary>
    public async Task<bool> IsValidGitHubUserAsync(string username)
    {
        if (_cache.TryGetValue(username, out var cached))
            return cached;
        var sharedKey = "github-user:" + username.ToLowerInvariant();
        if (_sharedCache != null && _sharedCache.TryGetValue(sharedKey, out bool shared))
            return _cache[username] = shared;

        try
        {
            // Through the same budget as every other GitHub call: mentions are written by package authors
            var url = $"https://api.github.com/users/{PackageUrlEncoder.EscapeSegment(username)}";
            if (_rateLimitHandler != null)
                await _rateLimitHandler.CanMakeRequestAsync();
            using var response = _rateLimiter != null
                ? await _rateLimiter.ExecuteAsync("github-api", async () => await _httpClient.GetAsync(url))
                : await _httpClient.GetAsync(url);
            if (_rateLimitHandler != null)
                await _rateLimitHandler.UpdateFromHeadersAsync(response);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Remember(username, sharedKey, false);
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Rate limited or failing: not known yet, so not accepted now and asked again on a later scrape
                _logger.LogDebug("[GitHubValidator] @{Username} could not be checked ({Status})", username, (int)response.StatusCode);
                return false;
            }

            // Parse the response body to check the account type
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            var accountType = doc.RootElement.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString()
                : null;

            // Only accept real User accounts — reject Organizations and Bots
            var isUser = string.Equals(accountType, "User", StringComparison.OrdinalIgnoreCase);

            Remember(username, sharedKey, isUser);
            _logger.LogDebug("[GitHubValidator] @{Username} → type={Type} → {Result}",
                username, accountType, isUser ? "accepted" : "rejected (org/bot)");

            return isUser;
        }
        catch (Exception ex)
        {
            // Not accepted: a name that cannot be checked is not shown as a contributor; a later scrape asks again
            _logger.LogWarning(ex, "[GitHubValidator] Could not validate @{Username}", username);
            return false;
        }
    }

    private void Remember(string username, string sharedKey, bool valid)
    {
        _cache[username] = valid;
        _sharedCache?.Set(sharedKey, valid, SharedLifetime);
    }
}
