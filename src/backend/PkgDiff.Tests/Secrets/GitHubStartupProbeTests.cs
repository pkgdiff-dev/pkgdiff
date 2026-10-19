using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PkgDiff.API.Services;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Secrets;

public class GitHubStartupProbeTests
{
    // Distinctive head and tail so any partial disclosure (old "abcd****wxyz" masking) is detectable.
    private const string TokenHead = "Qz7K";
    private const string TokenTail = "W3rY";
    private const string Token = TokenHead + "fake0test0token0value0only" + TokenTail;

    private const string AuthenticatedRateLimitBody =
        """{"rate":{"limit":5000,"remaining":4990,"reset":1790000000,"used":10}}""";

    [Fact]
    public async Task ConfiguredToken_ReportsConfiguredAndRateLimit_WithoutAnyTokenCharacters()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, AuthenticatedRateLimitBody));
        var logger = new CapturingLogger();

        await GitHubStartupProbe.LogStatusAsync(Configuration(Token), new StubHttpClientFactory(handler), logger);

        AssertNoTokenCharacters(logger);
        Assert.Contains(logger.Entries, e => e.Message.Contains("PersonalAccessTokenConfigured True"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("4990/5000"));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(GitHubStartupProbe.RateLimitUrl, request.Uri.ToString());
        Assert.Equal($"token {Token}", request.Authorization);
    }

    [Fact]
    public async Task RejectedToken_WarnsAboutValidity_WithoutAnyTokenCharacters()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""));
        var logger = new CapturingLogger();

        await GitHubStartupProbe.LogStatusAsync(Configuration(Token), new StubHttpClientFactory(handler), logger);

        AssertNoTokenCharacters(logger);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("rejected"));
    }

    [Fact]
    public async Task UnauthenticatedLimitWithToken_WarnsTokenNotAccepted()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"rate":{"limit":60,"remaining":59,"reset":1790000000,"used":1}}"""));
        var logger = new CapturingLogger();

        await GitHubStartupProbe.LogStatusAsync(Configuration(Token), new StubHttpClientFactory(handler), logger);

        AssertNoTokenCharacters(logger);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not being accepted"));
    }

    [Fact]
    public async Task NetworkFailure_IsNonFatalAndDoesNotLogTokenCharacters()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException($"connection failed for token {Token}"));
        var logger = new CapturingLogger();

        await GitHubStartupProbe.LogStatusAsync(Configuration(Token), new StubHttpClientFactory(handler), logger);

        AssertNoTokenCharacters(logger);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("HttpRequestException"));
    }

    [Fact]
    public async Task MissingToken_WarnsAndSkipsTheProbe()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, AuthenticatedRateLimitBody));
        var logger = new CapturingLogger();

        await GitHubStartupProbe.LogStatusAsync(Configuration(null), new StubHttpClientFactory(handler), logger);

        Assert.Empty(handler.Requests);
        Assert.Contains(logger.Entries, e => e.Message.Contains("PersonalAccessTokenConfigured False"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("No GitHub personal access token"));
    }

    private static void AssertNoTokenCharacters(CapturingLogger logger)
    {
        Assert.NotEmpty(logger.Entries);
        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain(TokenHead, entry.Message);
            Assert.DoesNotContain(TokenTail, entry.Message);
            Assert.DoesNotContain("****", entry.Message);
        }
    }

    private static IConfiguration Configuration(string? token)
    {
        var values = new Dictionary<string, string?>
        {
            ["GitHubSettings:EnableAuthentication"] = "true",
            ["GitHubSettings:UseGitHubApi"] = "true",
            ["GitHubSettings:UserAgent"] = "PkgDiff.Tests/1.0"
        };
        if (token != null)
        {
            values["GitHubSettings:PersonalAccessToken"] = token;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(Uri Uri, string? Authorization);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var authorization = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(",", values)
                : null;
            Requests.Add(new RecordedRequest(request.RequestUri!, authorization));
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpClientNames.GitHubApi, name);
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception) + " " + exception));
        }
    }
}
