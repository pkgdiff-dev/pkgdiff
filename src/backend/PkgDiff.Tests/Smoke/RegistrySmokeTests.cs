using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using PkgDiff.API.Extensions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Smoke;

public class RegistrySmokeTests
{
    private const string NpmAngularCoreUrl = "https://registry.npmjs.org/@angular%2Fcore";
    private const string PyPIRequestsUrl = "https://pypi.org/pypi/requests/json";
    private const string PubDevVersionsUrl = "https://pub.dev/packages/http/versions";
    private const string PubDevChangelogUrl = "https://pub.dev/packages/http/changelog";

    // Mirrors the defaults seeded by DatabaseSeeder, which keeps them private
    private static readonly string[] SeededExcludePatterns =
    [
        "dev", "ci", "build", "nightly", "snapshot", "local", "test", "rc", "alpha", "beta",
        "preview", "exp", "hotfix", "patch", "canary", "release", "stable", "final", "qa", "uat",
        "integration", "staging", "prod", "debug", "rc.", "pr", "feature", "bugfix", "release-branch", "m"
    ];

    private sealed class UrlMapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body, string ContentType)> _responses =
            new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => _requests.ToArray();

        public UrlMapHandler Map(
            string absoluteUri,
            string fixtureFile,
            string contentType,
            HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses[absoluteUri] = (status, ReadFixture(fixtureFile), contentType);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            _requests.Enqueue(uri);

            if (!_responses.TryGetValue(uri, out var recorded))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty),
                    RequestMessage = request
                });
            }

            return Task.FromResult(new HttpResponseMessage(recorded.Status)
            {
                Content = new StringContent(recorded.Body, Encoding.UTF8, recorded.ContentType),
                RequestMessage = request
            });
        }
    }

    private static string ReadFixture(string fixtureFile) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Smoke", "TestData", fixtureFile), Encoding.UTF8);

    private static ServiceProvider CreateProvider(UrlMapHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPkgDiffOutboundHttp();

        // Registered after the SsrfGuard default, so only the network hop is replaced by recorded responses
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider();
    }

    internal static async Task<VersionFilterService> CreateSeededVersionFilterAsync()
    {
        var unitOfWork = new FakeUnitOfWork();
        foreach (var pattern in SeededExcludePatterns)
        {
            await unitOfWork.VersionExcludePatterns.AddAsync(new VersionExcludePattern { Pattern = pattern, IsActive = true });
        }

        return new VersionFilterService(
            unitOfWork,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<VersionFilterService>.Instance);
    }

    [Fact]
    public async Task Npm_ScopedPackage_SmokeAnalysis()
    {
        var handler = new UrlMapHandler().Map(NpmAngularCoreUrl, "npm-angular-core.json", "application/json");
        await using var provider = CreateProvider(handler);
        var service = new NpmService(provider.GetRequiredService<IHttpClientFactory>());

        var inRange = await service.GetVersionsInRangeAsync("@angular/core", "21.0.0", "21.2.0");
        var stable = await service.GetPackageVersionsAsync("@angular/core");
        var metadata = await service.GetPackageMetadataAsync("@angular/core");
        var published = await service.GetPackagePublishedDateAsync("@angular/core", "21.2.0");

        Assert.Equal(new[] { "21.0.1", "21.1.0", "21.2.0" }, inRange);

        Assert.Equal(new[] { "20.3.14", "21.0.0", "21.0.1", "21.1.0", "21.2.0", "21.2.1", "22.2.1" }, stable);

        Assert.Equal("@angular/core", metadata.PackageId);
        Assert.Equal("22.2.1", metadata.LatestVersion);
        Assert.Equal("Angular - the core framework", metadata.Description);
        Assert.Equal("angular", metadata.Authors);
        Assert.Equal("https://github.com/angular/angular", metadata.GitHubRepositoryUrl);
        Assert.Equal(
            new[] { "22.2.1", "21.2.1", "21.2.0", "21.1.0", "21.0.1", "21.0.0", "20.3.14" },
            metadata.Versions.Select(v => v.Version));

        Assert.NotNull(published);
        Assert.Equal(new DateTime(2026, 2, 25, 19, 29, 37, 765, DateTimeKind.Utc), published.Value.ToUniversalTime());

        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, uri => Assert.Equal(NpmAngularCoreUrl, uri));
    }

    [Fact]
    public async Task PyPI_SmokeAnalysis()
    {
        var handler = new UrlMapHandler().Map(PyPIRequestsUrl, "pypi-requests.json", "application/json");
        await using var provider = CreateProvider(handler);
        var service = new PyPIService(provider.GetRequiredService<IHttpClientFactory>());

        var inRange = await service.GetVersionsInRangeAsync("requests", "2.31.0", "2.32.3");
        var stable = await service.GetPackageVersionsAsync("requests");
        var metadata = await service.GetPackageMetadataAsync("requests");
        var published = await service.GetPackagePublishedDateAsync("requests", "2.32.3");

        Assert.Equal(new[] { "2.32.0", "2.32.3" }, inRange);

        Assert.Equal(new[] { "2.4.0", "2.31.0", "2.32.0", "2.32.3", "2.34.2" }, stable);

        Assert.Equal("requests", metadata.PackageId);
        Assert.Equal("2.34.2", metadata.LatestVersion);
        Assert.Equal("Python HTTP for Humans.", metadata.Description);
        Assert.Equal("https://github.com/psf/requests", metadata.GitHubRepositoryUrl);
        Assert.Equal(
            new[] { "2.34.2", "2.32.3", "2.32.0", "2.31.0", "2.4.0" },
            metadata.Versions.Select(v => v.Version));

        Assert.Equal(new DateTime(2024, 5, 29, 15, 37, 47), published);

        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, uri => Assert.Equal(PyPIRequestsUrl, uri));
    }

    [Fact]
    public async Task PubDev_SmokeAnalysis()
    {
        var handler = new UrlMapHandler()
            .Map(PubDevVersionsUrl, "pubdev-http-versions.html", "text/html")
            .Map(PubDevChangelogUrl, "pubdev-http-changelog.html", "text/html");
        await using var provider = CreateProvider(handler);
        var service = provider.GetRequiredService<IPubDevService>();

        var versions = await service.GetAllVersionsAsync("http");
        var notes = await service.GetReleaseNotesAsync("http", "1.6.0");
        var olderNotes = await service.GetReleaseNotesAsync("http", "1.4.0");
        var unknownNotes = await service.GetReleaseNotesAsync("http", "0.0.1");

        Assert.Equal(
            new (string, DateTime?)[]
            {
                ("1.6.0", new DateTime(2025, 11, 10, 18, 27, 56, 434, DateTimeKind.Utc)),
                ("1.5.0", new DateTime(2025, 8, 7, 22, 35, 23, 863, DateTimeKind.Utc)),
                ("1.4.0", new DateTime(2025, 5, 6, 0, 17, 13, 636, DateTimeKind.Utc)),
                ("1.3.0", new DateTime(2025, 1, 21, 0, 32, 28, 622, DateTimeKind.Utc)),
                ("1.2.2", new DateTime(2024, 7, 16, 20, 9, 47, 863, DateTimeKind.Utc))
            },
            versions);
        Assert.All(versions, v => Assert.Equal(DateTimeKind.Utc, v.PublishDate!.Value.Kind));

        Assert.StartsWith("- **Breaking** Change the behavior of `Request.body` so that a charset", notes);
        Assert.Contains("- Export `MediaType` from `package:http_parser`.", notes);
        Assert.Equal(4, notes.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
        Assert.DoesNotContain("IOClient", notes);
        Assert.DoesNotContain("<", notes);

        Assert.Contains("- Avoid references to `window` in `BrowserClient`", olderNotes);
        Assert.Equal(string.Empty, unknownNotes);

        Assert.Equal(
            new[]
            {
                PubDevVersionsUrl,
                PubDevVersionsUrl + "/1.6.0/changelog", PubDevChangelogUrl,
                PubDevVersionsUrl + "/1.4.0/changelog", PubDevChangelogUrl,
                PubDevVersionsUrl + "/0.0.1/changelog", PubDevChangelogUrl
            },
            handler.Requests);
    }

    [Fact]
    public async Task NuGet_VersionParsing_SmokeAnalysis()
    {
        using var index = JsonDocument.Parse(ReadFixture("nuget-newtonsoft-json-index.json"));
        var listed = index.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToList();
        var filter = await CreateSeededVersionFilterAsync();
        var patterns = (await filter.GetActiveExcludePatternsAsync()).ToList();

        // Reversed first, so the order comes from NuGetVersion and not from the fixture
        var parsed = listed.AsEnumerable().Reverse().Select(NuGetVersion.Parse).OrderBy(v => v).ToList();
        var stable = parsed
            .Where(v => !v.IsPrerelease && !filter.ShouldExcludeVersion(v.ToString(), patterns))
            .ToList();
        var notExcluded = parsed
            .Where(v => !filter.ShouldExcludeVersion(v.ToString(), patterns))
            .ToList();

        var from = NuGetVersion.Parse("13.0.1");
        var to = NuGetVersion.Parse("13.0.3");
        var inRange = stable.Where(v => v > from && v <= to).OrderBy(v => v).Select(v => v.ToString());

        Assert.Equal(new[] { "13.0.2", "13.0.3" }, inRange);

        Assert.Equal(listed, parsed.Select(v => v.ToString()));
        Assert.Equal(listed.Where(v => !v.Contains('-')), stable.Select(v => v.ToString()));

        Assert.Contains("13.0.2-beta1", listed);
        Assert.Equal(stable, notExcluded);
    }
}
