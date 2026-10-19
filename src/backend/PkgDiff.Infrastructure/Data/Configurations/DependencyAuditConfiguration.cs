using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class DependencyAuditConfiguration : IEntityTypeConfiguration<DependencyAudit>
{
    public void Configure(EntityTypeBuilder<DependencyAudit> builder)
    {
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.Property(x => x.Registry).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Format).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.DependenciesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ReportJson).HasColumnType("jsonb");
        builder.Property(x => x.Error).HasMaxLength(1024);

        // The cleanup job removes expired reports; the start endpoint counts the ones still running
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.Status);
    }
}
