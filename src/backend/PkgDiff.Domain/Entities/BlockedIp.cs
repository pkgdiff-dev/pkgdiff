using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class BlockedIp : BaseEntity
{
    public string IpAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public BlockType BlockType { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public enum BlockType
{
    Temporary,
    Permanent
}
