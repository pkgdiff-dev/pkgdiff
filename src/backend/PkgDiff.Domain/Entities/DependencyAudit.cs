using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

// Found by random PublicId so report links cannot be guessed; the pasted file is never stored
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
