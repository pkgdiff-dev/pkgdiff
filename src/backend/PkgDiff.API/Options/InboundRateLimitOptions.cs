namespace PkgDiff.API.Options;

public sealed class InboundRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitTier General { get; set; } = new() { PerMinute = 60, PerFiveMinutes = 200 };

    // On top of the general tier: an export costs far more than an API call
    public RateLimitTier PdfExport { get; set; } = new() { PerMinute = 5, PerFiveMinutes = 15 };

    public RateLimitTier Admin { get; set; } = new() { PerMinute = 240, PerFiveMinutes = 1200 };

    public RateLimitTier Login { get; set; } = new() { PerMinute = 5, PerFiveMinutes = 15 };

    public RateLimitTier AuditStart { get; set; } = new() { PerMinute = 3, PerFiveMinutes = 6 };

    // One rendered page makes several API calls, so a crawler would otherwise run out of the visitor budget
    public RateLimitTier RenderServer { get; set; } = new() { PerMinute = 600, PerFiveMinutes = 2000 };

    // Empty: render server calls count as ordinary visitor calls
    public string RenderServerKey { get; set; } = string.Empty;

    public bool IsValid() =>
        General.IsValid() && PdfExport.IsValid() && Admin.IsValid() && Login.IsValid() && AuditStart.IsValid()
        && RenderServer.IsValid();
}

public sealed class RateLimitTier
{
    public int PerMinute { get; set; }
    public int PerFiveMinutes { get; set; }

    public bool IsValid() => PerMinute > 0 && PerFiveMinutes > 0;
}
