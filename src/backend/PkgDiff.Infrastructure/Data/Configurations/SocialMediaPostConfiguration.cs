using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class SocialMediaPostConfiguration : IEntityTypeConfiguration<SocialMediaPost>
{
    public void Configure(EntityTypeBuilder<SocialMediaPost> builder)
    {
        // SetNull keeps the social post history when an admin deletes the blog post it shared
        builder.HasOne(p => p.BlogPost)
               .WithMany()
               .HasForeignKey(p => p.BlogPostId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
