using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class ScrapeLogConfiguration : IEntityTypeConfiguration<ScrapeLog>
{
    public void Configure(EntityTypeBuilder<ScrapeLog> builder)
    {
        // The admin list is newest first
        builder.HasIndex(x => x.CreatedAt);

        // Failures of one package, newest first
        builder.HasIndex(x => new { x.PackageId, x.CreatedAt });
    }
}
