using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class ScrapeLog : BaseEntity
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Registry { get; set; } = "nuget";
    public string? GitHubRepoUrl { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string? AvailableVersions { get; set; }
    public string AttemptedSources { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
