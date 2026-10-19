using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data.Configurations;

public class VersionExcludePatternConfiguration : IEntityTypeConfiguration<VersionExcludePattern>
{
    public void Configure(EntityTypeBuilder<VersionExcludePattern> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Pattern)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Description)
            .HasMaxLength(200);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Seed data with common exclude patterns
        builder.HasData(
            new VersionExcludePattern { Id = 1, Pattern = "dev", Description = "Development builds (e.g., 1.0.0-dev-123)", IsActive = true },
            new VersionExcludePattern { Id = 2, Pattern = "ci", Description = "Continuous Integration builds (e.g., 1.0.0-ci-456)", IsActive = true },
            new VersionExcludePattern { Id = 3, Pattern = "build", Description = "Build number versions (e.g., 1.0.0-build-789)", IsActive = true },
            new VersionExcludePattern { Id = 4, Pattern = "nightly", Description = "Nightly builds (e.g., 1.0.0-nightly-20240101)", IsActive = true },
            new VersionExcludePattern { Id = 5, Pattern = "snapshot", Description = "Snapshot versions (e.g., 1.0.0-snapshot)", IsActive = true },
            new VersionExcludePattern { Id = 6, Pattern = "local", Description = "Local builds (e.g., 1.0.0-local)", IsActive = true },
            new VersionExcludePattern { Id = 7, Pattern = "test", Description = "Test builds (e.g., 1.0.0-test-123)", IsActive = true },
            new VersionExcludePattern { Id = 8, Pattern = "rc", Description = "Release Candidate versions (e.g., 1.0.0-rc1)", IsActive = true },
            new VersionExcludePattern { Id = 9, Pattern = "alpha", Description = "Alpha pre-release versions (e.g., 1.0.0-alpha)", IsActive = true },
            new VersionExcludePattern { Id = 10, Pattern = "beta", Description = "Beta pre-release versions (e.g., 1.0.0-beta)", IsActive = true },
            new VersionExcludePattern { Id = 11, Pattern = "preview", Description = "Preview versions (e.g., 1.0.0-preview1)", IsActive = true },
            new VersionExcludePattern { Id = 12, Pattern = "exp", Description = "Experimental builds (e.g., 1.0.0-exp)", IsActive = true },
            new VersionExcludePattern { Id = 13, Pattern = "hotfix", Description = "Hotfix builds (e.g., 1.0.1-hotfix)", IsActive = true },
            new VersionExcludePattern { Id = 14, Pattern = "patch", Description = "Patch builds (e.g., 1.0.0-patch-1)", IsActive = true },
            new VersionExcludePattern { Id = 15, Pattern = "canary", Description = "Canary builds (e.g., 1.0.0-canary)", IsActive = true },
            new VersionExcludePattern { Id = 16, Pattern = "release", Description = "Release versions (e.g., 1.0.0-release)", IsActive = true },
            new VersionExcludePattern { Id = 17, Pattern = "stable", Description = "Stable versions (e.g., 1.0.0-stable)", IsActive = true },
            new VersionExcludePattern { Id = 18, Pattern = "final", Description = "Final versions (e.g., 1.0.0-final)", IsActive = true },
            new VersionExcludePattern { Id = 19, Pattern = "qa", Description = "QA builds (e.g., 1.0.0-qa)", IsActive = true },
            new VersionExcludePattern { Id = 20, Pattern = "uat", Description = "UAT builds (e.g., 1.0.0-uat)", IsActive = true },
            new VersionExcludePattern { Id = 21, Pattern = "integration", Description = "Integration builds (e.g., 1.0.0-integration)", IsActive = true },
            new VersionExcludePattern { Id = 22, Pattern = "staging", Description = "Staging builds (e.g., 1.0.0-staging)", IsActive = true },
            new VersionExcludePattern { Id = 23, Pattern = "prod", Description = "Production builds (e.g., 1.0.0-prod)", IsActive = true },
            new VersionExcludePattern { Id = 24, Pattern = "debug", Description = "Debug builds (e.g., 1.0.0-debug)", IsActive = true },
            new VersionExcludePattern { Id = 25, Pattern = "rc.", Description = "Release Candidate shorthand (e.g., 1.0.0-rc.1)", IsActive = true },
            new VersionExcludePattern { Id = 26, Pattern = "pr", Description = "Pull Request builds (e.g., 1.0.0-pr-123)", IsActive = true },
            new VersionExcludePattern { Id = 27, Pattern = "feature", Description = "Feature branch builds (e.g., 1.0.0-feature-login)", IsActive = true },
            new VersionExcludePattern { Id = 28, Pattern = "bugfix", Description = "Bugfix branch builds (e.g., 1.0.0-bugfix-issue123)", IsActive = true },
            new VersionExcludePattern { Id = 29, Pattern = "release-branch", Description = "Release branch builds (e.g., 1.0.0-release-branch)", IsActive = true },
            new VersionExcludePattern { Id = 30, Pattern = "m", Description = "Milestone builds (e.g., 1.0.0-m1)", IsActive = true }
        );
    }
}
