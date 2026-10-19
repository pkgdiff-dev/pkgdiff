using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.TestDoubles;

// The four real registry clients over whatever a test supplies. A test that only needs a registry's
// rules (its version scheme, its label, its range) supplies nothing.
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
        // Metadata is cut by the exclude patterns, so a client always needs a filter; by default one without patterns
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
