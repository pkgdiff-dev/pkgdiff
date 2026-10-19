using System.Collections.Concurrent;
using AngleSharp.Dom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Outbound;

public class ScraperUrlTests
{
    private sealed class RecordingHtmlDocumentLoader : IHtmlDocumentLoader
    {
        private readonly ConcurrentQueue<string?> _urls = new();

        public IReadOnlyList<string?> Urls => _urls.ToArray();

        public Task<IDocument?> LoadAsync(string? url, CancellationToken cancellationToken = default)
        {
            _urls.Enqueue(url);
            return Task.FromResult<IDocument?>(null);
        }
    }

    private sealed class PassThroughRateLimiter : IRateLimiterService
    {
        public Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action) => action();

        public bool CanExecute(string key) => true;

        public TimeSpan GetTimeUntilNextSlot(string key) => TimeSpan.Zero;
    }

    private sealed class NoOpGitHubRateLimitHandler : IGitHubRateLimitHandler
    {
        public Task<bool> CanMakeRequestAsync() => Task.FromResult(true);

        public Task UpdateFromHeadersAsync(HttpResponseMessage response) => Task.CompletedTask;

        public Task<(int Remaining, int Limit, DateTime? ResetTime)> GetRateLimitStatusAsync()
            => Task.FromResult<(int, int, DateTime?)>((5000, 5000, null));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static IConfiguration CreateConfiguration(bool useGitHubApi) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitHubSettings:UseGitHubApi"] = useGitHubApi ? "true" : "false",
            ["GitHubSettings:EnableAuthentication"] = "true",
            ["GitHubSettings:HtmlScrapingTimeoutSeconds"] = "30"
        })
        .Build();

    private static GitHubScraperService CreateGitHubScraper(
        RecordingHtmlDocumentLoader loader,
        RecordingHandler handler,
        bool useGitHubApi = false) => new(
            new PassThroughRateLimiter(),
            new NoOpGitHubRateLimitHandler(),
            CreateConfiguration(useGitHubApi),
            new FakeHttpClientFactory(handler),
            NullLogger<GitHubScraperService>.Instance,
            loader,
            new GitHubScrapeCache());

    private static ScraperService CreateNuGetScraper(RecordingHtmlDocumentLoader loader, RecordingHandler handler) => new(
        new PassThroughRateLimiter(),
        NullLogger<ScraperService>.Instance,
        loader,
        new FakeHttpClientFactory(handler));

    [Fact]
    public async Task GitHubScraper_NuGetPage_EscapesPackageIdAndHandlesMissingPage()
    {
        var loader = new RecordingHtmlDocumentLoader();
        var scraper = CreateGitHubScraper(loader, new RecordingHandler());

        var result = await scraper.ExtractGitHubRepositoryUrlAsync("Foo/../Bar");

        Assert.Null(result);
        Assert.Equal(new[] { "https://www.nuget.org/packages/Foo%2F..%2FBar" }, loader.Urls);
    }

    [Fact]
    public async Task GitHubScraper_ReleasesPageThroughLoader_RawChangelogUrlsEscapeTheRef()
    {
        var loader = new RecordingHtmlDocumentLoader();
        var handler = new RecordingHandler();
        var scraper = CreateGitHubScraper(loader, handler);

        var notes = await scraper.GetReleaseNotesForVersionAsync("https://github.com/octo/escaped-ref-repo", "1.0#x");

        Assert.Null(notes);
        Assert.Equal(new[] { "https://github.com/octo/escaped-ref-repo/releases" }, loader.Urls);
        Assert.Contains(handler.Requests, r =>
            r.AbsoluteUri == "https://raw.githubusercontent.com/octo/escaped-ref-repo/v1.0%23x/CHANGELOG.md");
        Assert.All(handler.Requests, r => Assert.Equal("raw.githubusercontent.com", r.Host));
    }

    [Fact]
    public async Task GitHubScraper_ApiReleases_EscapeOwnerAndRepo()
    {
        var loader = new RecordingHtmlDocumentLoader();
        var handler = new RecordingHandler();
        var scraper = CreateGitHubScraper(loader, handler, useGitHubApi: true);

        var releases = await scraper.GetAllReleasesAsync("https://github.com/octo/ca;t");

        Assert.Empty(releases);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/octo/ca%3Bt/releases?per_page=100&page=1", request.AbsoluteUri);
    }

    [Fact]
    public async Task NuGetScraper_PackagePage_EscapesPackageIdAndHandlesMissingPage()
    {
        var loader = new RecordingHtmlDocumentLoader();
        var scraper = CreateNuGetScraper(loader, new RecordingHandler());

        var result = await scraper.GetGitHubRepositoryUrlAsync("Foo Bar");

        Assert.Null(result);
        Assert.Equal(new[] { "https://www.nuget.org/packages/Foo%20Bar" }, loader.Urls);
    }

    [Fact]
    public async Task NuGetScraper_ReleaseNotesPage_EscapesIdAndVersionAndHandlesMissingPage()
    {
        var loader = new RecordingHtmlDocumentLoader();
        var scraper = CreateNuGetScraper(loader, new RecordingHandler());

        var result = await scraper.GetReleaseNotesAsync("Foo/Bar", "1.0.0?x");

        Assert.Equal(string.Empty, result);
        Assert.Equal(new[] { "https://www.nuget.org/packages/Foo%2FBar/1.0.0%3Fx" }, loader.Urls);
    }
}
