using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

// One dependency audit: the packages a visitor pasted and, once the background job is done, the
// report. Found by PublicId (a random GUID) so a report link cannot be guessed. The pasted file
// itself is never stored, only the package names and versions taken from it.
public class DependencyAudit : BaseEntity
{
    public Guid PublicId { get; set; }
    public string Registry { get; set; } = string.Empty;
    public string? Format { get; set; }
    public string Status { get; set; } = "queued";
    public string DependenciesJson { get; set; } = "[]";
    public string? ReportJson { get; set; }
    public int Progress { get; set; }
    public int Total { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
