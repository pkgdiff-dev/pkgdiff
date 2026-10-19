using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Infrastructure.Data;
using Hangfire;

namespace PkgDiff.Infrastructure.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ISystemSettingsReader _settings;

    public HealthCheckService(
        ApplicationDbContext dbContext, 
        ILogger<HealthCheckService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ISystemSettingsReader settings)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.HealthProbe);
        _settings = settings;
    }

    public async Task<HealthStatus> GetOverallHealthAsync()
    {
        var details = await GetDetailedHealthAsync();
        return new HealthStatus(details.OverallStatus, details.CheckedAt);
    }

    public async Task<HealthDetails> GetDetailedHealthAsync()
    {
        var checkedAt = DateTime.UtcNow;

        // The settings come from the same DbContext as the database check, which allows one
        // operation at a time: they are read before the checks run in parallel
        var providerSettings = await ReadProviderSettingsAsync();

        // Run all checks in parallel
        var dbTask = CheckDatabaseHealthAsync();
        var nugetTask = CheckNuGetHealthAsync();
        var githubTask = CheckGitHubHealthAsync();
        var hangfireTask = CheckHangfireHealthAsync();

        var providersTask = CheckSecurityProvidersAsync(providerSettings);

        await Task.WhenAll(dbTask, nugetTask, githubTask, hangfireTask, providersTask);

        var dbHealth = await dbTask;
        var nugetHealth = await nugetTask;
        var githubHealth = await githubTask;
        var hangfireHealth = await hangfireTask;

        // Determine overall status
        var overallStatus = DetermineOverallStatus(dbHealth, nugetHealth, githubHealth, hangfireHealth);

        return new HealthDetails(
            overallStatus,
            dbHealth,
            nugetHealth,
            githubHealth,
            hangfireHealth,
            checkedAt,
            await providersTask
        );
    }

    private sealed record ProviderSettings(bool Osv, bool DepsDev, bool Socket, string? SocketToken);

    private async Task<ProviderSettings> ReadProviderSettingsAsync()
    {
        async Task<bool> EnabledAsync(string key)
        {
            var value = await _settings.GetAsync(key) ?? SettingKeys.Find(key)?.DefaultValue;
            return bool.TryParse(value, out var on) && on;
        }

        var osv = await EnabledAsync(SettingKeys.SecurityOsvEnabled);
        var depsDev = await EnabledAsync(SettingKeys.SecurityDepsDevEnabled);
        var socket = await EnabledAsync(SettingKeys.SecuritySocketEnabled);
        var token = await _settings.GetAsync(SettingKeys.SecuritySocketApiKey);
        return new ProviderSettings(osv, depsDev, socket, string.IsNullOrWhiteSpace(token) ? null : token.Trim());
    }

    // Not part of the overall status: the site works without security data
    private async Task<IReadOnlyList<SecurityProviderHealth>> CheckSecurityProvidersAsync(ProviderSettings settings)
    {
        var osv = ProbeAsync("OSV.dev", SettingKeys.SecurityOsvEnabled, settings.Osv,
            () => new HttpRequestMessage(HttpMethod.Get, "https://api.osv.dev/v1/vulns/GHSA-jfh8-c2jp-5v3q"));
        var depsDev = ProbeAsync("deps.dev", SettingKeys.SecurityDepsDevEnabled, settings.DepsDev,
            () => new HttpRequestMessage(HttpMethod.Get, "https://api.deps.dev/v3/systems/npm/packages/left-pad"));
        var socket = ProbeSocketAsync(settings);
        return await Task.WhenAll(osv, depsDev, socket);
    }

    private Task<SecurityProviderHealth> ProbeSocketAsync(ProviderSettings settings)
    {
        if (settings.SocketToken is null)
        {
            return Task.FromResult(new SecurityProviderHealth("Socket.dev", SettingKeys.SecuritySocketEnabled, settings.Socket, false, 0,
                settings.Socket ? "No API token is set (Security:Socket:ApiKey)" : null));
        }
        return ProbeAsync("Socket.dev", SettingKeys.SecuritySocketEnabled, settings.Socket, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.socket.dev/v0/quota");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SocketToken);
            return request;
        });
    }

    private async Task<SecurityProviderHealth> ProbeAsync(string name, string settingKey, bool enabled, Func<HttpRequestMessage> createRequest)
    {
        if (!enabled)
            return new SecurityProviderHealth(name, settingKey, false, false, 0, null);

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = createRequest();
            using var response = await _httpClient.SendAsync(request);
            sw.Stop();
            return new SecurityProviderHealth(name, settingKey, true, response.IsSuccessStatusCode, (int)sw.ElapsedMilliseconds,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[HealthCheck] {Provider}: unavailable", name);
            return new SecurityProviderHealth(name, settingKey, true, false, (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task<HangfireHealth> CheckHangfireHealthAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var monitoringApi = JobStorage.Current.GetMonitoringApi();
                
                // Get server count
                var servers = monitoringApi.Servers();
                var activeServers = servers.Count;
                
                // Get job stats
                var stats = monitoringApi.GetStatistics();
                
                // Determine health: considered healthy if at least 1 server is active
                // degraded if failed jobs > 50 (configurable threshold)
                var isHealthy = activeServers > 0;
                string? errorMessage = null;

                if (activeServers == 0)
                {
                    errorMessage = "No active Hangfire servers found";
                }
                else if (stats.Failed > 50)
                {
                    // Don't mark as unhealthy, just note it (status calculation will handle degraded)
                    errorMessage = $"High number of failed jobs: {stats.Failed}";
                }

                return new HangfireHealth(
                    isHealthy,
                    activeServers,
                    stats.Failed,
                    stats.Enqueued,
                    stats.Processing,
                    errorMessage
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HealthCheck] Hangfire: Unhealthy");
                return new HangfireHealth(false, 0, 0, 0, 0, ex.Message);
            }
        });
    }

    private async Task<DatabaseHealth> CheckDatabaseHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Simple connectivity check
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
            sw.Stop();

            _logger.LogDebug("[HealthCheck] Database: Healthy ({ResponseTime}ms)", sw.ElapsedMilliseconds);
            return new DatabaseHealth(true, (int)sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[HealthCheck] Database: Unhealthy");
            return new DatabaseHealth(false, (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task<ExternalServiceHealth> CheckNuGetHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.GetAsync("https://api.nuget.org/v3/index.json");
            sw.Stop();

            var isHealthy = response.IsSuccessStatusCode;
            _logger.LogDebug("[HealthCheck] NuGet API: {Status} ({ResponseTime}ms)", 
                isHealthy ? "Healthy" : "Degraded", sw.ElapsedMilliseconds);

            return new ExternalServiceHealth(
                isHealthy,
                (int)sw.ElapsedMilliseconds,
                null,
                isHealthy ? null : $"HTTP {(int)response.StatusCode}"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[HealthCheck] NuGet API: Unhealthy");
            return new ExternalServiceHealth(false, (int)sw.ElapsedMilliseconds, null, ex.Message);
        }
    }

    private async Task<ExternalServiceHealth> CheckGitHubHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Load GitHub settings
            var githubSettings = _configuration.GetSection("GitHubSettings");
            var personalAccessToken = githubSettings.GetValue<string?>("PersonalAccessToken");
            var enableAuthentication = githubSettings.GetValue<bool>("EnableAuthentication", true);
            var userAgent = githubSettings.GetValue<string>("UserAgent", "PkgDiff/1.0");
            
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/rate_limit");
            request.Headers.Add("User-Agent", userAgent);
            
            // Add authentication if token is provided and enabled
            var isAuthenticated = false;
            if (enableAuthentication && !string.IsNullOrWhiteSpace(personalAccessToken))
            {
                request.Headers.Add("Authorization", $"Bearer {personalAccessToken}");
                isAuthenticated = true;
            }

            _logger.LogDebug("[HealthCheck] GitHub API check using {Mode} mode", 
                isAuthenticated ? "authenticated" : "unauthenticated");

            using var response = await _httpClient.SendAsync(request);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var (rateLimitRemaining, rateLimitResetAt) = ParseGitHubRateLimit(content);

                _logger.LogDebug("[HealthCheck] GitHub API: Healthy ({ResponseTime}ms, {RateLimit} remaining, {Mode})",
                    sw.ElapsedMilliseconds, rateLimitRemaining,
                    isAuthenticated ? "authenticated" : "unauthenticated");

                return new ExternalServiceHealth(
                    true,
                    (int)sw.ElapsedMilliseconds,
                    rateLimitRemaining,
                    null,
                    rateLimitResetAt
                );
            }
            else
            {
                _logger.LogWarning("[HealthCheck] GitHub API: Degraded (HTTP {StatusCode})", (int)response.StatusCode);
                return new ExternalServiceHealth(
                    false,
                    (int)sw.ElapsedMilliseconds,
                    null,
                    $"HTTP {(int)response.StatusCode}"
                );
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[HealthCheck] GitHub API: Unhealthy");
            return new ExternalServiceHealth(false, (int)sw.ElapsedMilliseconds, null, ex.Message);
        }
    }

    // Example: {"resources":{"core":{"limit":5000,"remaining":4990,"reset":1790000000,"used":10}}}
    private static (int? Remaining, DateTime? ResetAt) ParseGitHubRateLimit(string jsonContent)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            if (!document.RootElement.TryGetProperty("resources", out var resources)
                || !resources.TryGetProperty("core", out var core))
                return (null, null);

            int? remaining = core.TryGetProperty("remaining", out var remainingProp) && remainingProp.TryGetInt32(out var r)
                ? r
                : null;
            DateTime? resetAt = core.TryGetProperty("reset", out var resetProp) && resetProp.TryGetInt64(out var reset)
                ? DateTimeOffset.FromUnixTimeSeconds(reset).UtcDateTime
                : null;
            return (remaining, resetAt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // The response does not have the expected shape
            return (null, null);
        }
    }

    private string DetermineOverallStatus(
        DatabaseHealth db, 
        ExternalServiceHealth nuget, 
        ExternalServiceHealth github,
        HangfireHealth hangfire)
    {
        // Database is critical
        if (!db.IsHealthy)
            return "Unhealthy";
        
        // Hangfire server is critical for background jobs
        if (!hangfire.IsHealthy)
            return "Unhealthy";

        // Check for degraded conditions
        if (hangfire.FailedJobs > 50)
            return "Degraded";

        // External services are less critical
        var externalServicesDown = (!nuget.IsAvailable ? 1 : 0) + (!github.IsAvailable ? 1 : 0);

        return externalServicesDown switch
        {
            0 => "Healthy",
            1 => "Degraded",
            _ => "Degraded"
        };
    }
}
