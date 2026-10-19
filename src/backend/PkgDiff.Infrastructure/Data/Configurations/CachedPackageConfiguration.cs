using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class CachedPackageConfiguration : IEntityTypeConfiguration<CachedPackage>
{
    public void Configure(EntityTypeBuilder<CachedPackage> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.PackageId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.RegistryId)
            .IsRequired();

        builder.HasOne(x => x.Registry)
            .WithMany()
            .HasForeignKey(x => x.RegistryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.LatestVersion)
            .HasMaxLength(50);

        builder.Property(x => x.PreviousLatestVersion)
            .HasMaxLength(50);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.Authors)
            .HasMaxLength(500);

        builder.Property(x => x.IconUrl)
            .HasMaxLength(500);

        builder.Property(x => x.ProjectUrl)
            .HasMaxLength(500);

        builder.Property(x => x.AllVersionsJson)
            .IsRequired()
            .HasColumnType("text");
            
        // Unique index on PackageId and RegistryId
        builder.HasIndex(x => new { x.PackageId, x.RegistryId })
            .IsUnique();
            
        // Index for expiration cleanup
        builder.HasIndex(x => x.ExpiresAt);

        // Index for the Live Updates feed order
        builder.HasIndex(x => x.LatestVersionDate);
        
        // Navigation
        builder.HasMany(x => x.ReleaseNotes)
            .WithOne(x => x.CachedPackage)
            .HasForeignKey(x => x.CachedPackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
