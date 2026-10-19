using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Tests.Outbound;

public class RegistryUrlEscapingTests
{
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly IReadOnlyDictionary<string, Uri> _baseAddresses;

        public FakeHttpClientFactory(HttpMessageHandler handler, IReadOnlyDictionary<string, Uri> baseAddresses)
        {
            _handler = handler;
            _baseAddresses = baseAddresses;
        }

        public List<string> RequestedNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            var client = new HttpClient(_handler, disposeHandler: false);
            if (_baseAddresses.TryGetValue(name, out var baseAddress))
            {
                client.BaseAddress = baseAddress;
            }

            return client;
        }
    }

    private static FakeHttpClientFactory CreateFactory(RecordingHandler handler) => new(handler, new Dictionary<string, Uri>
    {
        [HttpClientNames.NpmRegistry] = new("https://registry.npmjs.org/"),
        [HttpClientNames.PyPIRegistry] = new("https://pypi.org/pypi/")
    });

    [Theory]
    [InlineData("@angular/core", "@angular%2Fcore")]
    [InlineData("react", "react")]
    [InlineData("../../evil", "..%2F..%2Fevil")]
    [InlineData("a?b#c", "a%3Fb%23c")]
    [InlineData("@scope/../x", "@scope%2F..%2Fx")]
    public void EscapeNpmPackagePath_EscapesEverythingButTheScopeMarker(string packageId, string expected)
    {
        Assert.Equal(expected, PackageUrlEncoder.EscapeNpmPackagePath(packageId));
    }

    [Theory]
    [InlineData("octo/cat", "octo%2Fcat")]
    [InlineData("a b", "a%20b")]
    [InlineData("x?y#z", "x%3Fy%23z")]
    public void EscapeSegment_EscapesReservedCharacters(string value, string expected)
    {
        Assert.Equal(expected, PackageUrlEncoder.EscapeSegment(value));
    }

    [Fact]
    public async Task NpmService_ScopedPackage_RequestsSingleEscapedSegmentOnNamedClient()
    {
        var handler = new RecordingHandler();
        var factory = CreateFactory(handler);
        var service = new NpmService(factory);

        await service.GetPackageVersionsAsync("@angular/core");

        Assert.Contains(HttpClientNames.NpmRegistry, factory.RequestedNames);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://registry.npmjs.org/@angular%2Fcore", request.AbsoluteUri);
    }

    [Fact]
    public async Task NpmService_TraversalId_StaysOneSegmentUnderTheRegistryHost()
    {
        var handler = new RecordingHandler();
        var service = new NpmService(CreateFactory(handler));

        await service.GetPackageVersionsAsync("../../-/whoami");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("registry.npmjs.org", request.Host);
        Assert.Contains("..%2F..%2F-%2Fwhoami", request.AbsoluteUri);
        Assert.Equal("https://registry.npmjs.org/..%2F..%2F-%2Fwhoami", request.AbsoluteUri);
    }

    [Theory]
    [InlineData("a/../b", "https://pypi.org/pypi/a%2F..%2Fb/json")]
    [InlineData("requests", "https://pypi.org/pypi/requests/json")]
    [InlineData("x?y#z", "https://pypi.org/pypi/x%3Fy%23z/json")]
    public async Task PyPIService_RequestsEscapedIdOnNamedClient(string packageId, string expectedUri)
    {
        var handler = new RecordingHandler();
        var factory = CreateFactory(handler);
        var service = new PyPIService(factory);

        await service.GetPackageVersionsAsync(packageId);

        Assert.Contains(HttpClientNames.PyPIRegistry, factory.RequestedNames);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(expectedUri, request.AbsoluteUri);
    }

    [Fact]
    public async Task PubDevService_Changelog_EscapesPackageId()
    {
        var handler = new RecordingHandler();
        var service = new PubDevService(new HttpClient(handler), NullLogger<PubDevService>.Instance);

        await service.GetReleaseNotesAsync("../x", "1.0.0");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://pub.dev/packages/..%2Fx/changelog", request.AbsoluteUri);
    }

    [Fact]
    public async Task PubDevService_PackagePageAndVersions_EscapePackageId()
    {
        var handler = new RecordingHandler();
        var service = new PubDevService(new HttpClient(handler), NullLogger<PubDevService>.Instance);

        await service.GetGitHubRepositoryUrlAsync("a/b?c");
        await service.GetPackageExtraInfoAsync("a/b?c");
        await service.GetAllVersionsAsync("a/b?c");

        Assert.Equal(
            new[]
            {
                "https://pub.dev/packages/a%2Fb%3Fc",
                "https://pub.dev/packages/a%2Fb%3Fc",
                "https://pub.dev/packages/a%2Fb%3Fc/versions"
            },
            handler.Requests.Select(r => r.AbsoluteUri));
    }

    [Fact]
    public async Task GitHubUserValidator_EscapesUsername()
    {
        var handler = new RecordingHandler();
        var factory = CreateFactory(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubSettings:EnableAuthentication"] = "false"
            })
            .Build();
        var service = new GitHubUserValidatorService(factory, configuration, NullLogger<GitHubUserValidatorService>.Instance);

        var isValid = await service.IsValidGitHubUserAsync("octo/cat");

        Assert.False(isValid);
        Assert.Contains(HttpClientNames.GitHubApi, factory.RequestedNames);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/users/octo%2Fcat", request.AbsoluteUri);
    }
}
