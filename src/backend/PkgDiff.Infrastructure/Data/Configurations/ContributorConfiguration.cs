using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class ContributorConfiguration : IEntityTypeConfiguration<Contributor>
{
    public void Configure(EntityTypeBuilder<Contributor> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.GitHubUsername)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(c => c.GitHubUsername)
            .IsUnique();

        builder.Property(c => c.MentionCount)
            .HasDefaultValue(0);
    }
}
