namespace PkgDiff.Application.Interfaces;

public interface IHealthCheckService
{
    Task<HealthStatus> GetOverallHealthAsync();
    Task<HealthDetails> GetDetailedHealthAsync();
}

public record HealthStatus(string Status, DateTime CheckedAt);

public record HealthDetails(
    string OverallStatus,
    DatabaseHealth Database,
    ExternalServiceHealth NuGet,
    ExternalServiceHealth GitHub,
    HangfireHealth Hangfire,
    DateTime CheckedAt,
    IReadOnlyList<SecurityProviderHealth> SecurityProviders
);

// A provider of package security data; switched off ones are listed too, unchecked
public record SecurityProviderHealth(
    string Name,
    string SettingKey,
    bool Enabled,
    bool IsAvailable,
    int ResponseTimeMs,
    string? ErrorMessage
);

public record DatabaseHealth(bool IsHealthy, int ResponseTimeMs, string? ErrorMessage);

public record ExternalServiceHealth(
    bool IsAvailable,
    int ResponseTimeMs,
    int? RateLimitRemaining,
    string? ErrorMessage,
    DateTime? RateLimitResetAt = null
);

public record HangfireHealth(
    bool IsHealthy,
    int ActiveServers,
    long FailedJobs,
    long EnqueuedJobs,
    long ProcessingJobs,
    string? ErrorMessage
);
