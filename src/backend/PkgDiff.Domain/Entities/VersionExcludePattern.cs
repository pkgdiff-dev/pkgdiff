using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class VersionExcludePattern : BaseEntity
{
    public string Pattern { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
