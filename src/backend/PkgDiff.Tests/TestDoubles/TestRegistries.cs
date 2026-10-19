using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.TestDoubles;

public static class TestRegistries
{
    public static RegistryServiceFactory Create(
        INugetService? nuget = null,
        INpmService? npm = null,
        IPyPIService? pypi = null,
        IPubDevService? pubDev = null,
        IScraperService? nugetPage = null,
        IVersionFilterService? versionFilter = null,
        GitHubFirstScrapingStrategy? githubFirst = null,
        NuGetFirstScrapingStrategy? nugetFirst = null,
        PubDevScrapingStrategy? pubDevStrategy = null)
    {
        versionFilter ??= new VersionFilterService(
            new FakeUnitOfWork(), new MemoryCache(new MemoryCacheOptions()), NullLogger<VersionFilterService>.Instance);

        return new RegistryServiceFactory(
        [
            new NuGetRegistryClient(nuget!, nugetPage!, githubFirst!, nugetFirst!),
            new NpmRegistryClient(npm!, githubFirst!, versionFilter, NullLogger<NpmRegistryClient>.Instance),
            new PyPIRegistryClient(pypi!, githubFirst!, versionFilter, NullLogger<PyPIRegistryClient>.Instance),
            new PubDevRegistryClient(pubDev!, pubDevStrategy!, versionFilter, NullLogger<PubDevRegistryClient>.Instance)
        ]);
    }
}
