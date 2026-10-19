using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class CachedReleaseNoteConfiguration : IEntityTypeConfiguration<CachedReleaseNote>
{
    public void Configure(EntityTypeBuilder<CachedReleaseNote> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Version)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(x => x.ReleaseNotes)
            .HasColumnType("text");

        builder.Property(x => x.ParsedNotesJson)
            .HasColumnType("text");
            
        // One row per version of a package; lookups go through this index
        builder.HasIndex(x => new { x.CachedPackageId, x.Version })
            .IsUnique();
        
        // Index for expiration cleanup
        builder.HasIndex(x => x.ExpiresAt);
    }
}
