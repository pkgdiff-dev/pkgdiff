using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class PackageSecurityInfoConfiguration : IEntityTypeConfiguration<PackageSecurityInfo>
{
    public void Configure(EntityTypeBuilder<PackageSecurityInfo> builder)
    {
        builder.Property(x => x.Registry).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PackageKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PackageId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1024);

        builder.HasIndex(x => new { x.Registry, x.PackageKey }).IsUnique();

        // The refresh job looks for expired rows
        builder.HasIndex(x => x.ExpiresAt);
    }
}
