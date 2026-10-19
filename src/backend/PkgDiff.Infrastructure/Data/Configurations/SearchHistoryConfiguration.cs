using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class SearchHistoryConfiguration : IEntityTypeConfiguration<SearchHistory>
{
    public void Configure(EntityTypeBuilder<SearchHistory> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.PackageId)
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(x => x.FromVersion)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(x => x.ToVersion)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(x => x.IpAddress)
            .HasMaxLength(45); // IPv6 max length
            
        // Indexes for common queries
        builder.HasIndex(x => x.PackageId);
        builder.HasIndex(x => x.SearchedAt);
    }
}
