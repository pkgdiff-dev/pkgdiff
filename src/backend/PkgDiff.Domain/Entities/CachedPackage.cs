using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class CachedPackage : BaseEntity
{
    public string PackageId { get; set; } = string.Empty;
    public int RegistryId { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string? PreviousLatestVersion { get; set; }
    public DateTime? LatestVersionDate { get; set; } // Publish date of LatestVersion, or when it was first seen if the registry gave none
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? IconUrl { get; set; }
    public string? ProjectUrl { get; set; }
    public string AllVersionsJson { get; set; } = "[]"; // JSON serialized list
    public string? GitHubRepositoryUrl { get; set; }
    public string? ReleaseNotesSource { get; set; } // "NuGet" or "GitHub" or "PubDev"
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // When a visitor last opened or compared the package (at most hourly); null until then. Only packages
    // looked at recently are refreshed in the background.
    public DateTime? LastViewedAt { get; set; }

    // Navigation properties
    public Registry Registry { get; set; } = null!;
    public ICollection<CachedReleaseNote> ReleaseNotes { get; set; } = new List<CachedReleaseNote>();
}
