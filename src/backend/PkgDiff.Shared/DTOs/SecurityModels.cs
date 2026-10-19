namespace PkgDiff.Shared.DTOs;

public static class TrustStatus
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    // The providers are switched off, or the last refresh failed with nothing stored before it
    public const string Unavailable = "unavailable";
}

public static class AdvisorySeverity
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Unknown = "unknown";
}

// Served from a cache filled by a background job
public class PackageTrust
{
    public string Status { get; set; } = TrustStatus.Pending;
    public DateTime? FetchedAt { get; set; }

    public List<SecurityAdvisory> Advisories { get; set; } = new();

    // Checked version -> advisory ids (empty when clean); unchecked versions are absent
    public Dictionary<string, List<string>> VersionAdvisories { get; set; } = new();

    public PackageHealth? Health { get; set; }

    // null while the behaviour provider (Socket.dev) is switched off
    public Dictionary<string, BehaviorReport>? Behavior { get; set; }
}

public class SecurityAdvisory
{
    public string Id { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = AdvisorySeverity.Unknown;
    public List<string> FixedIn { get; set; } = new();
    public string Url { get; set; } = string.Empty;
    // OpenSSF malicious-packages record (MAL-...): malware, not merely vulnerable
    public bool IsMalicious { get; set; }
    public DateTime? Published { get; set; }
}

public class PackageHealth
{
    public string Source { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? Attribution { get; set; }
    public string? RepositoryUrl { get; set; }
    public double? ScorecardScore { get; set; }
    public DateTime? ScorecardDate { get; set; }
    public List<ScorecardCheck> Checks { get; set; } = new();
    public List<string> Licenses { get; set; } = new();
    // null when the source does not say
    public bool? HasProvenance { get; set; }
    public string? CheckedVersion { get; set; }
}

public class ScorecardCheck
{
    public string Name { get; set; } = string.Empty;
    public int? Score { get; set; }
    public string? Reason { get; set; }
}

public class BehaviorReport
{
    public double? OverallScore { get; set; }
    public double? SupplyChainScore { get; set; }
    public List<BehaviorAlert> Alerts { get; set; } = new();
    public string? Url { get; set; }
}

public class BehaviorAlert
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = AdvisorySeverity.Unknown;
}
