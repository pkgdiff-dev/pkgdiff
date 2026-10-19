using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class SearchHistory : BaseEntity
{
    public string PackageId { get; set; } = string.Empty;
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public bool IncludePrerelease { get; set; }
    public int TotalVersionsAnalyzed { get; set; }
    public DateTime SearchedAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
}
