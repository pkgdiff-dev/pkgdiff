using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Content;

public class SocialPostOutcomeTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private readonly ScriptedHandler _handler = new();
    private readonly SocialMediaService _service;

    public SocialPostOutcomeTests()
    {
        var reader = new SystemSettingsReader(_uow, _protector, NullLogger<SystemSettingsReader>.Instance);
        _service = new SocialMediaService(
            new FakeHttpClientFactory(_handler),
            _uow,
            reader,
            _protector,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SocialMediaService>.Instance);

        AddSetting(SettingKeys.TwitterApiKey, "k");
        AddSetting(SettingKeys.TwitterApiKeySecret, "ks");
        AddSetting(SettingKeys.TwitterAccessToken, "t");
        AddSetting(SettingKeys.TwitterAccessTokenSecret, "ts");
        AddSetting(SettingKeys.LinkedInAccessToken, "li");
        AddSetting(SettingKeys.LinkedInAuthorUrn, "urn:li:organization:1");
    }

    private void AddSetting(string key, string value)
    {
        var definition = SettingKeys.Find(key)!;
        _uow.SystemSettings.AddAsync(new SystemSettings
        {
            Key = key,
            Value = definition.IsSecret ? _protector.Protect(value) : value,
            DataType = definition.DataType,
            Category = definition.Category,
            IsSecret = definition.IsSecret
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Twitter_Timeout_IsUnknown()
    {
        _handler.Respond = _ => throw new TaskCanceledException("timed out");

        var result = await _service.PostToTwitterAsync("hello");

        Assert.Equal(SocialPostOutcome.Unknown, result.Outcome);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task LinkedIn_ConnectionReset_IsUnknown()
    {
        _handler.Respond = _ => throw new HttpRequestException("connection reset");

        var result = await _service.PostToLinkedInAsync("hello");

        Assert.Equal(SocialPostOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Twitter_SuccessWithUnparseableBody_IsSucceeded()
    {
        _handler.Respond = _ => Json(HttpStatusCode.Created, "not json");

        var result = await _service.PostToTwitterAsync("hello");

        Assert.Equal(SocialPostOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task Twitter_SuccessWithId_IsSucceededWithTheTweetUrl()
    {
        _handler.Respond = _ => Json(HttpStatusCode.Created, "{\"data\":{\"id\":\"42\"}}");

        var result = await _service.PostToTwitterAsync("hello");

        Assert.Equal(SocialPostOutcome.Succeeded, result.Outcome);
        Assert.Equal("https://twitter.com/user/status/42", result.PostUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ClientErrors_AreRejected(HttpStatusCode status)
    {
        _handler.Respond = _ => Json(status, "{}");

        Assert.Equal(SocialPostOutcome.Rejected, (await _service.PostToTwitterAsync("hello")).Outcome);
        Assert.Equal(SocialPostOutcome.Rejected, (await _service.PostToLinkedInAsync("hello")).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ServerErrors_AreUnknown(HttpStatusCode status)
    {
        _handler.Respond = _ => Json(status, "{}");

        Assert.Equal(SocialPostOutcome.Unknown, (await _service.PostToTwitterAsync("hello")).Outcome);
        Assert.Equal(SocialPostOutcome.Unknown, (await _service.PostToLinkedInAsync("hello")).Outcome);
    }

    [Fact]
    public async Task MissingCredentials_AreRejectedWithoutARequest()
    {
        await _uow.SystemSettings.DeleteAsync(_uow.SystemSettings.Items.Single(s => s.Key == SettingKeys.LinkedInAccessToken));

        var result = await _service.PostToLinkedInAsync("hello");

        Assert.Equal(SocialPostOutcome.Rejected, result.Outcome);
        Assert.Equal(0, _handler.Calls);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Respond(request));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
