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
        services.AddPkgDiffOutboundHttp();

        services.AddPkgDiffSecrets();

        services.AddSingleton<IRateLimiterService, RateLimiterService>();
        services.AddSingleton<IGitHubRateLimitHandler, GitHubRateLimitHandler>();
        services.AddSingleton<IWorkTrackerService, WorkTrackerService>();

        services.AddScoped<INugetService, NugetService>();
        services.AddScoped<INpmService, NpmService>();
        services.AddScoped<IPyPIService, PyPIService>();
        services.AddScoped<IPackagistService, PackagistService>();
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

        services.AddScoped<IScrapeLogService, ScrapeLogService>();

        services.AddScoped<GitHubFirstScrapingStrategy>();
        services.AddScoped<NuGetFirstScrapingStrategy>();
        services.AddScoped<PubDevScrapingStrategy>();

        services.AddScoped<IRegistryClient, NuGetRegistryClient>();
        services.AddScoped<IRegistryClient, NpmRegistryClient>();
        services.AddScoped<IRegistryClient, PyPIRegistryClient>();
        services.AddScoped<IRegistryClient, PubDevRegistryClient>();
        services.AddScoped<IRegistryClient, PackagistRegistryClient>();

        services.AddScoped<IRegistryServiceFactory, RegistryServiceFactory>();
        services.AddScoped<IPackageCacheService, PackageCacheService>();
        services.AddScoped<ILiveUpdateService, LiveUpdateService>();
        services.AddScoped<ICachedLookups, CachedLookups>();

        services.AddSingleton<IReleaseNotesRenderer, ReleaseNotesRenderer>();
        services.AddScoped<PackageAnalysisService>(); // Concrete class for Hangfire
        services.AddScoped<IPackageAnalysisService, PackageAnalysisService>();
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

        // The Ollama typed client is registered by AddPkgDiffOutboundHttp
        services.AddScoped<IAutoPilotService, AutoPilotService>();
        services.AddScoped<ISocialMediaService, SocialMediaService>();
        services.AddScoped<BlogSocialPostJob>();

        return services;
    }
}
