using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class CachedReleaseNote : BaseEntity
{
    public int CachedPackageId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string? ParsedNotesJson { get; set; }
    public string? ReleaseNotesSource { get; set; }
    public DateTime? PublishedDate { get; set; }
    public bool IsEmpty { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public CachedPackage CachedPackage { get; set; } = null!;
}
