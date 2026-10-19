namespace PkgDiff.API.Options;

public sealed class InboundRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitTier General { get; set; } = new() { PerMinute = 60, PerFiveMinutes = 200 };

    // Counted on its own, on top of the general tier: an export is far more expensive than an API call
    public RateLimitTier PdfExport { get; set; } = new() { PerMinute = 5, PerFiveMinutes = 15 };

    // The admin panel and the Hangfire dashboard poll, so signed-in work needs more room than a visitor
    public RateLimitTier Admin { get; set; } = new() { PerMinute = 240, PerFiveMinutes = 1200 };

    // Sign-in attempts per address: tight, so guessing passwords is slow
    public RateLimitTier Login { get; set; } = new() { PerMinute = 5, PerFiveMinutes = 15 };

    // Each audit checks up to the package limit against OSV: a few per address are plenty
    public RateLimitTier AuditStart { get; set; } = new() { PerMinute = 3, PerFiveMinutes = 6 };

    public bool IsValid() =>
        General.IsValid() && PdfExport.IsValid() && Admin.IsValid() && Login.IsValid() && AuditStart.IsValid();
}

public sealed class RateLimitTier
{
    public int PerMinute { get; set; }
    public int PerFiveMinutes { get; set; }

    public bool IsValid() => PerMinute > 0 && PerFiveMinutes > 0;
}
