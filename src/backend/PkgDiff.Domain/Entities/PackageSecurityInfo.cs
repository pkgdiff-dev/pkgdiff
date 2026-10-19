using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

// The PackageTrust DTO as one JSON document, always read and written whole
public class PackageSecurityInfo : BaseEntity
{
    public string Registry { get; set; } = string.Empty;
    // Lower-cased so ids differing only in case share one row
    public string PackageKey { get; set; } = string.Empty;
    // The spelling the package was first asked for with, which is how its cache row is found
    public string PackageId { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTime? FetchedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    // When a refresh was last queued, so a busy page does not queue one per visitor
    public DateTime? RequestedAt { get; set; }
    public string? LastError { get; set; }
}
