using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class SystemSettings : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DataType { get; set; } = "String"; // String, Int, Bool, Json
    public string Category { get; set; } = "General"; // e.g. General, AutoPilot, SocialMedia:Twitter, SocialMedia:LinkedIn
    public bool IsSecret { get; set; } = false; // If true, frontend will mask the value
}

