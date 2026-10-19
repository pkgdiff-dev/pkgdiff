using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
