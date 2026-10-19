using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(x => x.Value)
            .IsRequired()
            .HasColumnType("text");
            
        builder.Property(x => x.Description)
            .HasMaxLength(500);
            
        builder.Property(x => x.DataType)
            .HasMaxLength(50);
            
        builder.HasIndex(x => x.Key).IsUnique();
    }
}
