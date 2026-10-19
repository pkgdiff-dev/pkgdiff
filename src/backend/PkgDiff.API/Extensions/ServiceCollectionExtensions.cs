using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Jobs;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Infrastructure.Services;
using PkgDiff.API.Services;

namespace PkgDiff.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // SSRF-guarded defaults for every HttpClient, the named clients and the Ollama exception
        services.AddPkgDiffOutboundHttp();

        // Secret settings protector (key from Security:SettingsEncryptionKey) and decrypting settings reader
        services.AddPkgDiffSecrets();

        // Rate Limiting (Singleton - global state)
        services.AddSingleton<IRateLimiterService, RateLimiterService>();
        services.AddSingleton<IGitHubRateLimitHandler, GitHubRateLimitHandler>();
        services.AddSingleton<IWorkTrackerService, WorkTrackerService>();

        // Core Registry Services
        services.AddScoped<INugetService, NugetService>();
        services.AddScoped<INpmService, NpmService>();
        services.AddScoped<IPyPIService, PyPIService>();
        // IPubDevService is the PubDev typed client registered by AddPkgDiffOutboundHttp
        services.AddScoped<IScraperService, ScraperService>();
        services.AddSingleton<GitHubScrapeCache>();
        services.AddScoped<IGitHubScraperService, GitHubScraperService>();
        services.AddScoped<IAnalyzerService, AnalyzerService>();
        services.AddScoped<IVersionFilterService, VersionFilterService>();
        services.AddHttpClient<IReleaseNotesUrlResolver, ReleaseNotesUrlResolver>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "PkgDiff/1.0");
        });

        // Scrape Log Service
        services.AddScoped<IScrapeLogService, ScrapeLogService>();

        // Scraping Strategies
        services.AddScoped<GitHubFirstScrapingStrategy>();
        services.AddScoped<NuGetFirstScrapingStrategy>();
        services.AddScoped<PubDevScrapingStrategy>();

        // One client per registry, found by name through the factory
        services.AddScoped<IRegistryClient, NuGetRegistryClient>();
        services.AddScoped<IRegistryClient, NpmRegistryClient>();
        services.AddScoped<IRegistryClient, PyPIRegistryClient>();
        services.AddScoped<IRegistryClient, PubDevRegistryClient>();

        // Factory & Cache Services
        services.AddScoped<IRegistryServiceFactory, RegistryServiceFactory>();
        services.AddScoped<IPackageCacheService, PackageCacheService>();
        services.AddScoped<ILiveUpdateService, LiveUpdateService>();
        services.AddScoped<ICachedLookups, CachedLookups>();

        // Analysis Services
        services.AddSingleton<IReleaseNotesRenderer, ReleaseNotesRenderer>();
        services.AddScoped<PackageAnalysisService>(); // Concrete class for Hangfire
        services.AddScoped<IPackageAnalysisService, PackageAnalysisService>(); // Interface
        services.AddScoped<IProgressReporter, SignalrProgressReporter>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IOsvClient, OsvClient>();
        services.AddScoped<IPackageHealthClient, PackageHealthClient>();
        services.AddScoped<IBehaviorClient, SocketBehaviorClient>();
        services.AddScoped<IPackageTrustService, PackageTrustService>();
        services.AddSingleton<IManifestParser, ManifestParser>();
        services.AddScoped<IDependencyAuditService, DependencyAuditService>();

        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminPackageService, AdminPackageService>();
        services.AddScoped<IAdminSettingsService, AdminSettingsService>();
        services.AddScoped<IAdminJobService, AdminJobService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();
        services.AddScoped<IAdminScrapeLogService, AdminScrapeLogService>();
        services.AddScoped<IAdminSecurityService, AdminSecurityService>();
        services.AddScoped<IHealthCheckService, HealthCheckService>();
        services.AddScoped<IContributorService, ContributorService>();
        services.AddScoped<GitHubUserValidatorService>();
        services.AddScoped<AuthService>();

        // AutoPilot & LLM (the Ollama typed client is registered by AddPkgDiffOutboundHttp)
        services.AddScoped<IAutoPilotService, AutoPilotService>();
        services.AddScoped<ISocialMediaService, SocialMediaService>();
        services.AddScoped<BlogSocialPostJob>();

        return services;
    }
}
