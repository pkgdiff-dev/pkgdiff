using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class Contributor : BaseEntity
{
    public string GitHubUsername { get; set; } = string.Empty;
    public int MentionCount { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
