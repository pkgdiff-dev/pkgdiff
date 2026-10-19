using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder)
    {
        builder.ToTable("BlogPosts");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title).IsRequired().HasMaxLength(255);
        builder.Property(b => b.Slug).IsRequired().HasMaxLength(255);
        builder.Property(b => b.Content).IsRequired();
        builder.Property(b => b.PackageId).IsRequired().HasMaxLength(150);
        builder.Property(b => b.Version).IsRequired().HasMaxLength(50);
        builder.Property(b => b.Registry).IsRequired().HasMaxLength(20);
        builder.Property(b => b.LlmModelUsed).HasMaxLength(100);

        builder.HasIndex(b => b.Slug).IsUnique();
        builder.HasIndex(b => b.CachedReleaseNoteId).IsUnique();
        builder.HasIndex(b => b.CreatedAt);
        builder.HasIndex(b => b.PublishedAt);

        builder.HasOne(b => b.CachedReleaseNote)
               .WithMany()
               .HasForeignKey(b => b.CachedReleaseNoteId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
