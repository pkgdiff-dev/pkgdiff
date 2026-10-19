namespace PkgDiff.Shared.DTOs;

public static class TrustStatus
{
    // No security data yet: a background job is collecting it
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

// The security picture of one package: known vulnerabilities per version (OSV) and the health of
// its project (deps.dev, OpenSSF Scorecard). Served from a cache filled by a background job.
public class PackageTrust
{
    public string Status { get; set; } = TrustStatus.Pending;
    public DateTime? FetchedAt { get; set; }

    // Every advisory that affects at least one checked version, once
    public List<SecurityAdvisory> Advisories { get; set; } = new();

    // Version string (as the registry spells it) -> ids of the advisories that affect it. A checked
    // version without advisories has an empty list; a version that was not checked is missing.
    public Dictionary<string, List<string>> VersionAdvisories { get; set; } = new();

    public PackageHealth? Health { get; set; }

    // Behaviour analysis per version (Socket.dev); null while that provider is switched off
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
    // An OpenSSF malicious-packages record (id MAL-...): the version is malware, not merely vulnerable
    public bool IsMalicious { get; set; }
    public DateTime? Published { get; set; }
}

public class PackageHealth
{
    // "deps.dev" or "OpenSSF Scorecard": where the figures come from, shown with its attribution
    public string Source { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? Attribution { get; set; }
    public string? RepositoryUrl { get; set; }
    public double? ScorecardScore { get; set; }
    public DateTime? ScorecardDate { get; set; }
    public List<ScorecardCheck> Checks { get; set; } = new();
    public List<string> Licenses { get; set; } = new();
    // null = the source does not say; true/false = a provenance attestation was or was not found
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
