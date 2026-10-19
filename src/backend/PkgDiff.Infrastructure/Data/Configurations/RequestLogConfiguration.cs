using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class RequestLogConfiguration : IEntityTypeConfiguration<RequestLog>
{
    public void Configure(EntityTypeBuilder<RequestLog> builder)
    {
        // Time-range statistics and the daily cleanup
        builder.HasIndex(x => x.CreatedAt);

        // Per-address activity over a time range
        builder.HasIndex(x => new { x.IpAddress, x.CreatedAt });
    }
}
