namespace PkgDiff.Shared.DTOs;

public class LiveUpdate
{
    public string PackageId { get; set; } = string.Empty;
    public string Registry { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
}
