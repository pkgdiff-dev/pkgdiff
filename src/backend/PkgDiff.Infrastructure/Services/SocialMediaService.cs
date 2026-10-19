using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class SocialMediaService : ISocialMediaService
{
    private const string OAuthStateKeyPrefix = "linkedin-oauth-state:";
    private const string InvalidStateMessage = "Invalid or expired LinkedIn authorization state.";
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISystemSettingsReader _settingsReader;
    private readonly ISecretProtector _secretProtector;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SocialMediaService> _logger;

    public SocialMediaService(
        IHttpClientFactory httpClientFactory,
        IUnitOfWork unitOfWork,
        ISystemSettingsReader settingsReader,
        ISecretProtector secretProtector,
        IDistributedCache cache,
        ILogger<SocialMediaService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _unitOfWork = unitOfWork;
        _settingsReader = settingsReader;
        _secretProtector = secretProtector;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync(PkgDiff.Domain.Entities.SocialMediaPlatform platform)
    {
        string?[] required = platform switch
        {
            PkgDiff.Domain.Entities.SocialMediaPlatform.Twitter =>
            [
                await _settingsReader.GetAsync(SettingKeys.TwitterApiKey),
                await _settingsReader.GetAsync(SettingKeys.TwitterApiKeySecret),
                await _settingsReader.GetAsync(SettingKeys.TwitterAccessToken),
                await _settingsReader.GetAsync(SettingKeys.TwitterAccessTokenSecret)
            ],
            PkgDiff.Domain.Entities.SocialMediaPlatform.LinkedIn =>
            [
                await _settingsReader.GetAsync(SettingKeys.LinkedInAccessToken),
                await _settingsReader.GetAsync(SettingKeys.LinkedInAuthorUrn)
            ],
            _ => [null]
        };

        return required.All(value => !string.IsNullOrEmpty(value));
    }

    public async Task<SocialPostResult> PostToTwitterAsync(string content)
    {
        const string requestUrl = "https://api.twitter.com/2/tweets";
        HttpClient client;
        StringContent contentPayload;

        try
        {
            var apiKey = await _settingsReader.GetAsync(SettingKeys.TwitterApiKey);
            var apiKeySecret = await _settingsReader.GetAsync(SettingKeys.TwitterApiKeySecret);
            var accessToken = await _settingsReader.GetAsync(SettingKeys.TwitterAccessToken);
            var accessTokenSecret = await _settingsReader.GetAsync(SettingKeys.TwitterAccessTokenSecret);

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiKeySecret) ||
                string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(accessTokenSecret))
            {
                return SocialPostResult.Rejected("Twitter API credentials are not fully configured in System Settings.");
            }

            client = _httpClientFactory.CreateClient("Twitter");
            contentPayload = new StringContent(JsonSerializer.Serialize(new { text = content }), Encoding.UTF8, "application/json");

            // Generate OAuth 1.0a Authorization header
            string authHeader = GenerateTwitterOAuthHeader(requestUrl, "POST", apiKey, apiKeySecret, accessToken, accessTokenSecret);
            client.DefaultRequestHeaders.Add("Authorization", authHeader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare the Twitter post");
            return SocialPostResult.Rejected(ex.Message);
        }

        var sent = await SendAsync(client, requestUrl, contentPayload, "Twitter");
        if (sent.Result is not null)
            return sent.Result;

        using var response = sent.Response!;
        if (response.IsSuccessStatusCode)
        {
            // The platform accepted the post; a body we cannot parse must not turn it into a retried failure
            try
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var tweetId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
                return SocialPostResult.Succeeded($"https://twitter.com/user/status/{tweetId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Twitter accepted the post but its response could not be read");
                return SocialPostResult.Succeeded(null);
            }
        }

        return await FailureFromResponseAsync(response, "Twitter");
    }

    public async Task<SocialPostResult> PostToLinkedInAsync(string content)
    {
        const string requestUrl = "https://api.linkedin.com/rest/posts";
        HttpClient client;
        StringContent contentPayload;

        try
        {
            var accessToken = await _settingsReader.GetAsync(SettingKeys.LinkedInAccessToken);
            var authorURN = await _settingsReader.GetAsync(SettingKeys.LinkedInAuthorUrn); // e.g. urn:li:organization:1234567 or urn:li:person:abcdef

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(authorURN))
            {
                return SocialPostResult.Rejected("LinkedIn API credentials (AccessToken or AuthorURN) are not configured in System Settings.");
            }

            client = _httpClientFactory.CreateClient("LinkedIn");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
            client.DefaultRequestHeaders.Add("LinkedIn-Version", "202602"); // Latest active version (Feb 2026)

            var payload = new
            {
                author = authorURN,
                commentary = content,
                visibility = "PUBLIC",
                distribution = new
                {
                    feedDistribution = "MAIN_FEED",
                    targetEntities = Array.Empty<object>(),
                    thirdPartyDistributionChannels = Array.Empty<object>()
                },
                lifecycleState = "PUBLISHED",
                isReshareDisabledByAuthor = false
            };

            contentPayload = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare the LinkedIn post");
            return SocialPostResult.Rejected(ex.Message);
        }

        var sent = await SendAsync(client, requestUrl, contentPayload, "LinkedIn");
        if (sent.Result is not null)
            return sent.Result;

        using var response = sent.Response!;
        if (response.IsSuccessStatusCode)
        {
            // LinkedIn returns 201 Created and Post ID in the x-restli-id header
            if (response.Headers.TryGetValues("x-restli-id", out var values))
            {
                var postId = values.FirstOrDefault();
                return SocialPostResult.Succeeded($"https://www.linkedin.com/feed/update/{postId}");
            }
            return SocialPostResult.Succeeded("https://www.linkedin.com/company/pkgdiff"); // Fallback URL
        }

        return await FailureFromResponseAsync(response, "LinkedIn");
    }

    // Once the request has started, a timeout or broken connection says nothing about whether the platform published it
    private async Task<(HttpResponseMessage? Response, SocialPostResult? Result)> SendAsync(
        HttpClient client,
        string requestUrl,
        HttpContent content,
        string platform)
    {
        try
        {
            return (await client.PostAsync(requestUrl, content), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {Platform} post request did not complete; its outcome is unknown", platform);
            return (null, SocialPostResult.Unknown($"The {platform} request did not complete, so it is unknown whether the post was published: {ex.Message}"));
        }
    }

    // A 4xx is a definite refusal; a 5xx may come from a gateway after the platform already processed the post
    private async Task<SocialPostResult> FailureFromResponseAsync(HttpResponseMessage response, string platform)
    {
        string responseContent;
        try
        {
            responseContent = await response.Content.ReadAsStringAsync();
        }
        catch (Exception)
        {
            responseContent = string.Empty;
        }

        _logger.LogError("{Platform} API Error {StatusCode}: {Response}", platform, (int)response.StatusCode, responseContent);
        var message = $"{platform} API returned {response.StatusCode}: {responseContent}";
        return (int)response.StatusCode >= 500 ? SocialPostResult.Unknown(message) : SocialPostResult.Rejected(message);
    }

    private string GenerateTwitterOAuthHeader(string url, string method, string apiKey, string apiSecret, string accessToken, string accessTokenSecret)
    {
        var timestamp = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString();
        var nonce = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())).Replace("=", "").Replace("+", "").Replace("/", "");

        var oauthParams = new SortedDictionary<string, string>
        {
            { "oauth_consumer_key", apiKey },
            { "oauth_nonce", nonce },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", timestamp },
            { "oauth_token", accessToken },
            { "oauth_version", "1.0" }
        };

        var parameterString = string.Join("&", oauthParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var signatureBaseString = $"{method.ToUpperInvariant()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(parameterString)}";
        var signingKey = $"{Uri.EscapeDataString(apiSecret)}&{Uri.EscapeDataString(accessTokenSecret)}";

        using var hasher = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
        var signatureBytes = hasher.ComputeHash(Encoding.ASCII.GetBytes(signatureBaseString));
        var signature = Convert.ToBase64String(signatureBytes);

        oauthParams.Add("oauth_signature", signature);

        return "OAuth " + string.Join(", ", oauthParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}=\"{Uri.EscapeDataString(kvp.Value)}\""));
    }

    public async Task<(string AuthUrl, string State)?> GetLinkedInAuthUrlAsync(string redirectUri, string userId)
    {
        var clientId = await _settingsReader.GetAsync(SettingKeys.LinkedInClientId);
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning("LinkedIn ClientId is not configured in System Settings.");
            return null;
        }

        // The state binds the callback to this admin and this redirect URI (login-CSRF protection)
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await _cache.SetStringAsync(
            OAuthStateKeyPrefix + state,
            JsonSerializer.Serialize(new OAuthStateEntry(userId, redirectUri)),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = OAuthStateLifetime });

        // Only request standard scopes that are available to all LinkedIn apps
        // w_member_social: post on behalf of member, openid/profile/email: basic profile
        var scopes = "openid profile email w_member_social";
        var authUrl = $"https://www.linkedin.com/oauth/v2/authorization" +
                      $"?response_type=code" +
                      $"&client_id={Uri.EscapeDataString(clientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope={Uri.EscapeDataString(scopes)}" +
                      $"&state={state}";

        return (authUrl, state);
    }

    public async Task<(bool Success, string? ErrorMessage)> HandleLinkedInCallbackAsync(string code, string state, string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                _logger.LogWarning("LinkedIn OAuth callback rejected: code or state missing");
                return (false, InvalidStateMessage);
            }

            var stateKey = OAuthStateKeyPrefix + state;
            var stored = await _cache.GetStringAsync(stateKey);
            var entry = stored == null ? null : JsonSerializer.Deserialize<OAuthStateEntry>(stored);
            if (entry == null)
            {
                _logger.LogWarning("LinkedIn OAuth callback rejected: state unknown, expired or already used");
                return (false, InvalidStateMessage);
            }

            if (!string.Equals(entry.UserId, userId, StringComparison.Ordinal))
            {
                // Not removed: a foreign caller must not be able to burn the owner's pending state
                _logger.LogWarning("LinkedIn OAuth callback rejected: state was issued to a different user");
                return (false, InvalidStateMessage);
            }

            // Single use: consumed before the exchange so a replay fails even if the exchange fails
            await _cache.RemoveAsync(stateKey);
            var redirectUri = entry.RedirectUri;

            var clientId = await _settingsReader.GetAsync(SettingKeys.LinkedInClientId);
            var clientSecret = await _settingsReader.GetAsync(SettingKeys.LinkedInClientSecret);

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                return (false, "LinkedIn ClientId or ClientSecret is not configured in System Settings.");

            // Step 1: Exchange authorization code for access token
            var tokenClient = _httpClientFactory.CreateClient();
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", redirectUri },
                { "client_id", clientId },
                { "client_secret", clientSecret }
            });

            var tokenResponse = await tokenClient.PostAsync("https://www.linkedin.com/oauth/v2/accessToken", tokenRequest);
            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorCode = TryReadOAuthError(tokenContent);
                _logger.LogWarning("LinkedIn token exchange failed with {StatusCode} ({Error})", (int)tokenResponse.StatusCode, errorCode ?? "no error code");
                return (false, $"LinkedIn token exchange failed ({errorCode ?? tokenResponse.StatusCode.ToString()}).");
            }

            using var tokenDoc = JsonDocument.Parse(tokenContent);
            var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenProp)
                ? accessTokenProp.GetString()
                : null;

            if (string.IsNullOrEmpty(accessToken))
                return (false, "Access token was empty in LinkedIn's response.");

            // Step 2: Find the managed company page (pkgdiff)
            var orgClient = _httpClientFactory.CreateClient();
            orgClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            orgClient.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
            orgClient.DefaultRequestHeaders.Add("LinkedIn-Version", "202602"); // Latest active version (Feb 2026)

            // Get organizations where the user is an admin
            var orgsResponse = await orgClient.GetAsync("https://api.linkedin.com/v2/organizationAcls?q=roleAssignee&role=ADMINISTRATOR&state=APPROVED&count=50");
            var orgsContent = await orgsResponse.Content.ReadAsStringAsync();

            string? authorUrn = null;
            if (orgsResponse.IsSuccessStatusCode)
            {
                using var orgsDoc = JsonDocument.Parse(orgsContent);
                var elements = orgsDoc.RootElement.GetProperty("elements");

                // Try to find pkgdiff org, otherwise fall back to first admin org
                foreach (var element in elements.EnumerateArray())
                {
                    if (element.TryGetProperty("organization", out var orgProp))
                    {
                        var orgUrn = orgProp.GetString();
                        if (!string.IsNullOrEmpty(orgUrn))
                        {
                            // Fetch org details to find pkgdiff
                            var orgDetailResp = await orgClient.GetAsync($"https://api.linkedin.com/v2/organizations/{orgUrn.Split(':').Last()}");
                            if (orgDetailResp.IsSuccessStatusCode)
                            {
                                var orgDetailContent = await orgDetailResp.Content.ReadAsStringAsync();
                                using var orgDetailDoc = JsonDocument.Parse(orgDetailContent);
                                if (orgDetailDoc.RootElement.TryGetProperty("vanityName", out var vanity) &&
                                    string.Equals(vanity.GetString(), "pkgdiff", StringComparison.OrdinalIgnoreCase))
                                {
                                    authorUrn = orgUrn;
                                    break;
                                }
                            }

                            // Fallback: Use first org if pkgdiff not found yet
                            authorUrn ??= orgUrn;
                        }
                    }
                }
            }

            // Step 3: Save AccessToken and AuthorURN to SystemSettings
            await SaveOrUpdateSettingAsync(SettingKeys.LinkedInAccessToken, accessToken, "LinkedIn OAuth Access Token (auto-generated)");

            if (!string.IsNullOrEmpty(authorUrn))
                await SaveOrUpdateSettingAsync(SettingKeys.LinkedInAuthorUrn, authorUrn, "LinkedIn Company Author URN (auto-resolved)");

            _logger.LogInformation("LinkedIn OAuth completed. AccessToken saved. AuthorURN: {URN}", authorUrn ?? "not found");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LinkedIn OAuth callback");
            return (false, "LinkedIn authorization could not be completed.");
        }
    }

    private static string? TryReadOAuthError(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveOrUpdateSettingAsync(string key, string value, string description)
    {
        var definition = SettingKeys.Find(key);
        var isSecret = definition?.IsSecret ?? false;
        var storedValue = isSecret ? _secretProtector.Protect(value) : value;

        var existing = (await _unitOfWork.SystemSettings.FindAsync(s => s.Key == key)).FirstOrDefault();
        if (existing != null)
        {
            existing.Value = storedValue;
            await _unitOfWork.SystemSettings.UpdateAsync(existing);
        }
        else
        {
            await _unitOfWork.SystemSettings.AddAsync(new PkgDiff.Domain.Entities.SystemSettings
            {
                Key = key,
                Value = storedValue,
                Description = definition?.Description ?? description,
                DataType = definition?.DataType ?? "String",
                Category = definition?.Category ?? "General",
                IsSecret = isSecret
            });
        }
        await _unitOfWork.SaveChangesAsync();
    }

    private sealed record OAuthStateEntry(string UserId, string RedirectUri);
}
