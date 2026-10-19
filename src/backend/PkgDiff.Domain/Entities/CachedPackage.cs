using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class CachedPackage : BaseEntity
{
    public string PackageId { get; set; } = string.Empty;
    public int RegistryId { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string? PreviousLatestVersion { get; set; }
    public DateTime? LatestVersionDate { get; set; } // Falls back to first-seen time when the registry gives none
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? IconUrl { get; set; }
    public string? ProjectUrl { get; set; }
    public string AllVersionsJson { get; set; } = "[]";
    public string? GitHubRepositoryUrl { get; set; }
    public string? ReleaseNotesSource { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Updated at most hourly; only recently viewed packages are refreshed in the background
    public DateTime? LastViewedAt { get; set; }

    public Registry Registry { get; set; } = null!;
    public ICollection<CachedReleaseNote> ReleaseNotes { get; set; } = new List<CachedReleaseNote>();
}
