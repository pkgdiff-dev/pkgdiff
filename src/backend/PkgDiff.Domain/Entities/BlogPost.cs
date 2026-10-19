using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class BlogPost : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Registry { get; set; } = "nuget";
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public string LlmModelUsed { get; set; } = string.Empty;

    public int? CachedReleaseNoteId { get; set; }
    public CachedReleaseNote? CachedReleaseNote { get; set; }
}
