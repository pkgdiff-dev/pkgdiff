using Hangfire;
using Microsoft.EntityFrameworkCore;
using PkgDiff.API.Jobs;
using PkgDiff.API.Services;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Jobs;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.API.Extensions;

// What the host does once before it serves traffic
public static class StartupTasks
{
    private const string DefaultAutoPilotCron = "0 8,14,20 * * *";

    // The refresh job is what finds new versions for Live Updates; without the setting it still runs, every six hours
    private const string DefaultCacheRefreshCron = "0 */6 * * *";

    // A database that cannot be migrated stops the application
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();
            logger.LogInformation("Database migrations completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed; the application will not start");
            throw;
        }
    }

    // Logs GitHub configuration and token validity; never any token characters
    public static async Task LogGitHubStatusAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            await GitHubStartupProbe.LogStatusAsync(
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
                logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading GitHub settings");
        }
    }

    // Any failure (missing ADMIN_PASSWORD, Identity error) stops startup
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await DatabaseSeeder.SeedDataAsync(scope.ServiceProvider);
    }

    public static async Task ScheduleRecurringJobsAsync(this WebApplication app)
    {
        RecurringJob.AddOrUpdate<PackageAnalysisJob>(
            "package-analysis-job",
            job => job.ExecuteAsync(),
            app.Configuration["BackgroundJobs:CacheRefreshCron"] ?? DefaultCacheRefreshCron);

        // Security data of packages people looked at, refreshed in the background before it is served stale
        RecurringJob.AddOrUpdate<IPackageTrustService>(
            "security-refresh",
            job => job.RefreshExpiredAsync(),
            "15 */6 * * *");

        // Dependency audit reports are kept for 30 days
        RecurringJob.AddOrUpdate<IDependencyAuditService>(
            "dependency-audit-cleanup",
            job => job.DeleteExpiredAsync(),
            Cron.Daily);

        RecurringJob.AddOrUpdate<DatabaseCleanupJob>(
            "cleanup-request-logs",
            job => job.CleanupOldRequestLogsAsync(),
            Cron.Daily);

        RecurringJob.AddOrUpdate<IAutoPilotService>(
            "autopilot-blog-generator",
            job => job.RunAutoPilotJobAsync(),
            await ReadAutoPilotCronAsync(app));
    }

    private static async Task<string> ReadAutoPilotCronAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var frequencySetting = await context.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "AutoPilot.DailyFrequency");

        return frequencySetting != null && int.TryParse(frequencySetting.Value, out var frequency)
            ? AdminSettingsService.GetCronExpressionForFrequency(frequency)
            : DefaultAutoPilotCron;
    }
}
