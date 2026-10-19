using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.Constants;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.OAuth;

public class LinkedInOAuthTests
{
    private const string RedirectUri = "https://pkgdiff.dev/admin/social-media/linkedin/callback";
    private const string TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";

    private readonly FakeUnitOfWork _uow = new();
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly RecordingHandler _handler = new();
    private readonly SocialMediaService _service;

    public LinkedInOAuthTests()
    {
        var reader = new SystemSettingsReader(_uow, _protector, NullLogger<SystemSettingsReader>.Instance);
        _service = new SocialMediaService(
            new FakeHttpClientFactory(_handler),
            _uow,
            reader,
            _protector,
            _cache,
            NullLogger<SocialMediaService>.Instance);
    }

    private SystemSettings AddSetting(string key, string value)
    {
        var definition = SettingKeys.Find(key)!;
        var entity = new SystemSettings
        {
            Key = key,
            Value = definition.IsSecret ? _protector.Protect(value) : value,
            Description = definition.Description,
            DataType = definition.DataType,
            Category = definition.Category,
            IsSecret = definition.IsSecret
        };
        _uow.SystemSettings.AddAsync(entity).GetAwaiter().GetResult();
        return entity;
    }

    private void AddLinkedInApp()
    {
        AddSetting(SettingKeys.LinkedInClientId, "client-id");
        AddSetting(SettingKeys.LinkedInClientSecret, "client-secret");
    }

    private static string StateKey(string state) => $"linkedin-oauth-state:{state}";

    private async Task<string> IssueStateAsync(string userId)
    {
        var result = await _service.GetLinkedInAuthUrlAsync(RedirectUri, userId);
        Assert.NotNull(result);
        return result.Value.State;
    }

    [Fact]
    public async Task GetAuthUrl_WithClientId_StoresRandomStateInCache_AndEmbedsItInTheUrl()
    {
        AddLinkedInApp();

        var result = await _service.GetLinkedInAuthUrlAsync(RedirectUri, "u1");

        Assert.NotNull(result);
        var (authUrl, state) = result.Value;
        Assert.Matches(new Regex("^[0-9A-Fa-f]{64}$"), state);
        Assert.Contains("state=" + state, authUrl, StringComparison.Ordinal);
        Assert.NotNull(await _cache.GetStringAsync(StateKey(state), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAuthUrl_IssuesADifferentStateEveryTime()
    {
        AddLinkedInApp();

        var first = await IssueStateAsync("u1");
        var second = await IssueStateAsync("u1");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task GetAuthUrl_WithoutClientId_ReturnsNull()
    {
        var result = await _service.GetLinkedInAuthUrlAsync(RedirectUri, "u1");

        Assert.Null(result);
    }

    [Fact]
    public async Task Callback_UnknownState_IsRejected_WithoutCallingLinkedIn()
    {
        AddLinkedInApp();

        var (success, error) = await _service.HandleLinkedInCallbackAsync("code", "unknown-state", "u1");

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("", "some-state")]
    [InlineData("code", "")]
    public async Task Callback_MissingCodeOrState_IsRejected_WithoutCallingLinkedIn(string code, string state)
    {
        AddLinkedInApp();

        var (success, _) = await _service.HandleLinkedInCallbackAsync(code, state, "u1");

        Assert.False(success);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Callback_StateIssuedToAnotherUser_IsRejected_AndLeavesTheStateForItsOwner()
    {
        AddLinkedInApp();
        var state = await IssueStateAsync("u1");

        var (success, _) = await _service.HandleLinkedInCallbackAsync("code", state, "u2");

        Assert.False(success);
        Assert.Empty(_handler.Requests);
        Assert.NotNull(await _cache.GetStringAsync(StateKey(state), TestContext.Current.CancellationToken));

        var (ownerSuccess, _) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");
        Assert.True(ownerSuccess);
    }

    [Fact]
    public async Task Callback_SameUser_Succeeds_UsesStoredRedirectUri_AndConsumesTheState()
    {
        AddLinkedInApp();
        var state = await IssueStateAsync("u1");

        var (success, error) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");

        Assert.True(success, error);
        var tokenRequest = Assert.Single(_handler.Requests, r => r.Uri == TokenEndpoint);
        var form = HttpUtility.ParseQueryString(tokenRequest.Body);
        Assert.Equal(RedirectUri, form["redirect_uri"]);
        Assert.Equal("client-secret", form["client_secret"]);
        Assert.Null(await _cache.GetStringAsync(StateKey(state), TestContext.Current.CancellationToken));

        var requestsBeforeReplay = _handler.Requests.Count;
        var (replaySuccess, _) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");

        Assert.False(replaySuccess);
        Assert.Equal(requestsBeforeReplay, _handler.Requests.Count);
    }

    [Fact]
    public async Task Callback_Replay_IsRejected_EvenWhenTheFirstExchangeFailed()
    {
        AddLinkedInApp();
        _handler.TokenStatus = HttpStatusCode.BadRequest;
        var state = await IssueStateAsync("u1");

        var (firstSuccess, _) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");
        Assert.False(firstSuccess);
        Assert.Single(_handler.Requests);
        Assert.Null(await _cache.GetStringAsync(StateKey(state), TestContext.Current.CancellationToken));

        _handler.TokenStatus = HttpStatusCode.OK;
        var (replaySuccess, _) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");

        Assert.False(replaySuccess);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Callback_Success_StoresTheAccessTokenEncrypted_WithKnownKeyMetadata()
    {
        AddLinkedInApp();
        var state = await IssueStateAsync("u1");

        var (success, error) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");

        Assert.True(success, error);
        var row = Assert.Single(_uow.SystemSettings.Items, s => s.Key == SettingKeys.LinkedInAccessToken);
        Assert.StartsWith("enc:v1:", row.Value, StringComparison.Ordinal);
        Assert.Equal("tok", _protector.Unprotect(row.Value));
        Assert.Equal("Social Media: LinkedIn", row.Category);
        Assert.True(row.IsSecret);
    }

    [Fact]
    public async Task Callback_Success_ReplacesAnExistingAccessTokenRow_Encrypted()
    {
        AddLinkedInApp();
        var existing = AddSetting(SettingKeys.LinkedInAccessToken, "old-token");
        var state = await IssueStateAsync("u1");

        var (success, error) = await _service.HandleLinkedInCallbackAsync("code", state, "u1");

        Assert.True(success, error);
        Assert.Single(_uow.SystemSettings.Items, s => s.Key == SettingKeys.LinkedInAccessToken);
        Assert.StartsWith("enc:v1:", existing.Value, StringComparison.Ordinal);
        Assert.Equal("tok", _protector.Unprotect(existing.Value));
    }

    [Fact]
    public async Task PostToTwitter_WithEncryptedCredentials_SignsWithTheDecryptedApiKey()
    {
        AddSetting(SettingKeys.TwitterApiKey, "twitter-api-key");
        AddSetting(SettingKeys.TwitterApiKeySecret, "twitter-api-key-secret");
        AddSetting(SettingKeys.TwitterAccessToken, "twitter-access-token");
        AddSetting(SettingKeys.TwitterAccessTokenSecret, "twitter-access-token-secret");

        await _service.PostToTwitterAsync("hello");

        var request = Assert.Single(_handler.Requests);
        Assert.NotNull(request.Authorization);
        Assert.Contains("oauth_consumer_key=\"twitter-api-key\"", request.Authorization, StringComparison.Ordinal);
        Assert.Contains("oauth_token=\"twitter-access-token\"", request.Authorization, StringComparison.Ordinal);
        Assert.DoesNotContain("enc%3Av1%3A", request.Authorization, StringComparison.Ordinal);
    }

    private sealed record RecordedRequest(string Method, string Uri, string? Authorization, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.GetLeftPart(UriPartial.Path);
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : null;
            Requests.Add(new RecordedRequest(request.Method.Method, uri, authorization, body));

            if (uri == TokenEndpoint)
            {
                if (TokenStatus != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(TokenStatus)
                    {
                        Content = new StringContent("{\"error\":\"invalid_request\"}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
