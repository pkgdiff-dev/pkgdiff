using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public enum SocialMediaPlatform
{
    Twitter = 1,
    LinkedIn = 2
}

public enum SocialMediaPostStatus
{
    Pending = 1,
    Success = 2,
    Failed = 3
}

public class SocialMediaPost : BaseEntity
{
    public SocialMediaPlatform Platform { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? PostUrl { get; set; }
    public SocialMediaPostStatus Status { get; set; } = SocialMediaPostStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Optional reference to a blog post if it was auto-generated
    public int? BlogPostId { get; set; }
    public BlogPost? BlogPost { get; set; }
}
