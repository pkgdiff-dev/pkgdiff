using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using PkgDiff.API.Extensions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Smoke;

public class LiveRegistrySmokeTests
{
    private const string OptInVariable = "PKGDIFF_LIVE_SMOKE";

    // The default suite never touches the network; PKGDIFF_LIVE_SMOKE=1 opts in
    private static void SkipUnlessOptedIn() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            "set PKGDIFF_LIVE_SMOKE=1 to call the real registries");

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPkgDiffOutboundHttp();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Npm_ScopedPackage_LiveSmoke()
    {
        SkipUnlessOptedIn();
        await using var provider = CreateProvider();
        var service = new NpmService(provider.GetRequiredService<IHttpClientFactory>());

        var versions = (await service.GetVersionsInRangeAsync("@angular/core", "21.0.0", "21.2.0")).ToList();

        Assert.NotEmpty(versions);
        Assert.Contains("21.1.0", versions);
        Assert.Equal("21.2.0", versions[^1]);
        Assert.DoesNotContain("21.0.0", versions);
        Assert.DoesNotContain(versions, v => v.Contains('-'));
        Assert.Equal(versions.OrderBy(Version.Parse), versions);
    }

    [Fact]
    public async Task PyPI_LiveSmoke()
    {
        SkipUnlessOptedIn();
        await using var provider = CreateProvider();
        var service = new PyPIService(provider.GetRequiredService<IHttpClientFactory>());

        var versions = (await service.GetVersionsInRangeAsync("requests", "2.31.0", "2.32.3")).ToList();

        Assert.NotEmpty(versions);
        Assert.Equal("2.32.3", versions[^1]);
        Assert.DoesNotContain("2.31.0", versions);
        Assert.Equal(versions.OrderBy(Version.Parse), versions);
    }

    [Fact]
    public async Task PubDev_LiveSmoke()
    {
        SkipUnlessOptedIn();
        await using var provider = CreateProvider();
        var service = provider.GetRequiredService<IPubDevService>();

        var versions = await service.GetAllVersionsAsync("http");

        Assert.NotEmpty(versions);
        Assert.Contains(versions, v => v.Version == "1.6.0");
        Assert.All(versions, v => Assert.NotNull(v.PublishDate));

        var notes = await service.GetReleaseNotesAsync("http", versions[0].Version);

        Assert.False(string.IsNullOrWhiteSpace(notes));
    }

    [Fact]
    public async Task NuGet_LiveSmoke()
    {
        SkipUnlessOptedIn();
        var filter = await RegistrySmokeTests.CreateSeededVersionFilterAsync();
        var service = new NugetService(filter, NullLogger<NugetService>.Instance);

        var versions = await service.GetVersionsInRangeAsync(
            "Newtonsoft.Json",
            NuGetVersion.Parse("13.0.1"),
            NuGetVersion.Parse("13.0.3"));

        Assert.Equal(new[] { "13.0.2", "13.0.3" }, versions.Select(v => v.ToString()));
    }
}
