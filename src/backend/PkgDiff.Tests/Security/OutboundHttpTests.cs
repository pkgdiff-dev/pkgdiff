using System.Net;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.API.Extensions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Security;

public class OutboundHttpTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://127.0.0.1:1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPkgDiffOutboundHttp();
        return services.BuildServiceProvider();
    }

    private static async Task<HttpRequestException> GetFailureAsync(HttpClient client, string url)
    {
        return await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnnamedDefaultClient_Loopback_IsBlocked()
    {
        using var provider = BuildProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient();

        var ex = await GetFailureAsync(client, "http://127.0.0.1/");

        Assert.IsType<SsrfBlockedException>(ex.InnerException);
    }

    [Theory]
    [InlineData(HttpClientNames.GitHubApi)]
    [InlineData(HttpClientNames.ExternalHtml)]
    [InlineData(HttpClientNames.NpmRegistry)]
    [InlineData(HttpClientNames.PyPIRegistry)]
    [InlineData(HttpClientNames.PubDev)]
    public async Task NamedClients_CloudMetadata_IsBlocked(string clientName)
    {
        using var provider = BuildProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        var ex = await GetFailureAsync(client, "http://169.254.169.254/latest/meta-data");

        Assert.IsType<SsrfBlockedException>(ex.InnerException);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("https://127.0.0.1:8443/")]
    public async Task UnnamedDefaultClient_EncodedInternalTargets_AreBlocked(string url)
    {
        using var provider = BuildProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient();

        var ex = await GetFailureAsync(client, url);

        Assert.IsType<SsrfBlockedException>(ex.InnerException);
    }

    [Fact]
    public async Task OllamaTypedClient_IsTheSingleUnguardedException()
    {
        using var provider = BuildProvider();
        // Typed clients are registered under the short type name of the client interface.
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(IOllamaService).Name);

        var ex = await GetFailureAsync(client, "http://127.0.0.1:1/");

        Assert.IsNotType<SsrfBlockedException>(ex.InnerException);
    }

    [Theory]
    [InlineData(null, 15, 2L * 1024 * 1024)]
    [InlineData(HttpClientNames.ExternalHtml, 15, 2L * 1024 * 1024)]
    [InlineData(HttpClientNames.NpmRegistry, 30, 64L * 1024 * 1024)]
    [InlineData(HttpClientNames.PyPIRegistry, 30, 64L * 1024 * 1024)]
    [InlineData(HttpClientNames.GitHubApi, 30, 16L * 1024 * 1024)]
    [InlineData(HttpClientNames.PubDev, 30, 8L * 1024 * 1024)]
    public void ClientLimits_AreResolvedPerClient(string? clientName, int timeoutSeconds, long maxBufferBytes)
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientName is null ? factory.CreateClient() : factory.CreateClient(clientName);

        Assert.Equal(TimeSpan.FromSeconds(timeoutSeconds), client.Timeout);
        Assert.Equal(maxBufferBytes, client.MaxResponseContentBufferSize);
    }

    [Fact]
    public void PubDevService_IsResolvedAsTheGuardedPubDevTypedClient()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IPubDevService>();

        Assert.IsType<PkgDiff.Infrastructure.Services.PubDevService>(service);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<Uri?> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public List<string> RequestedNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private static HtmlDocumentLoader CreateLoader(HttpMessageHandler handler, out FakeHttpClientFactory factory)
    {
        factory = new FakeHttpClientFactory(handler);
        return new HtmlDocumentLoader(factory, NullLogger<HtmlDocumentLoader>.Instance);
    }

    private static HttpResponseMessage HtmlResponse(HttpRequestMessage request, string html, Uri? finalUri = null)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
            RequestMessage = finalUri is null ? request : new HttpRequestMessage(HttpMethod.Get, finalUri)
        };
    }

    private const string ReleasesHtml =
        "<html><body><a href=\"/owner/repo/pull/1\">PR</a><a href=\"?page=2\">Next</a></body></html>";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("ftp://x/y")]
    [InlineData("file:///etc/passwd")]
    public async Task LoadAsync_InvalidOrNonHttpUrl_ReturnsNullWithoutRequest(string? url)
    {
        var handler = new RecordingHandler(request => HtmlResponse(request, ReleasesHtml));
        var loader = CreateLoader(handler, out _);

        var document = await loader.LoadAsync(url, TestContext.Current.CancellationToken);

        Assert.Null(document);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_HtmlPage_ResolvesRelativeLinksAgainstRequestUrl()
    {
        var handler = new RecordingHandler(request => HtmlResponse(request, ReleasesHtml));
        var loader = CreateLoader(handler, out var factory);

        var document = await loader.LoadAsync(
            "https://example.test/owner/repo/releases",
            TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.Equal("https://example.test/owner/repo/releases", document.Url);
        var hrefs = document.QuerySelectorAll("a").OfType<IHtmlAnchorElement>().Select(a => a.Href).ToList();
        Assert.Equal(
            new[] { "https://example.test/owner/repo/pull/1", "https://example.test/owner/repo/releases?page=2" },
            hrefs);
        Assert.Equal(new[] { HttpClientNames.ExternalHtml }, factory.RequestedNames);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_AfterRedirect_UsesFinalRequestUri()
    {
        var finalUri = new Uri("https://example.test/moved/releases");
        var handler = new RecordingHandler(request => HtmlResponse(request, ReleasesHtml, finalUri));
        var loader = CreateLoader(handler, out _);

        var document = await loader.LoadAsync(
            "https://example.test/owner/repo/releases",
            TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.Equal(finalUri.ToString(), document.Url);
        var first = document.QuerySelectorAll("a").OfType<IHtmlAnchorElement>().First();
        Assert.Equal("https://example.test/owner/repo/pull/1", first.Href);
    }

    [Fact]
    public async Task LoadAsync_NotFound_ReturnsNull()
    {
        var handler = new RecordingHandler(request => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = request,
            Content = new StringContent("missing")
        });
        var loader = CreateLoader(handler, out _);

        var document = await loader.LoadAsync("https://example.test/missing", TestContext.Current.CancellationToken);

        Assert.Null(document);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_BlockedFetch_ReturnsNullWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException(
            "blocked",
            new SsrfBlockedException("Outbound connection to internal.test:80 blocked: non-public destination.")));
        var loader = CreateLoader(handler, out _);

        var document = await loader.LoadAsync("http://internal.test/", TestContext.Current.CancellationToken);

        Assert.Null(document);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_WithRealRegistration_LoopbackTarget_ReturnsNull()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IHtmlDocumentLoader>();

        var document = await loader.LoadAsync("http://127.0.0.1/secret", TestContext.Current.CancellationToken);

        Assert.Null(document);
    }
}
