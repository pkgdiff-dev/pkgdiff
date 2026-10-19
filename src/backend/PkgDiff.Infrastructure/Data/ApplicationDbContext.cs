using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PkgDiff.Domain.Entities;

namespace PkgDiff.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<SearchHistory> SearchHistories { get; set; }
    public DbSet<CachedPackage> CachedPackages { get; set; }
    public DbSet<CachedReleaseNote> CachedReleaseNotes { get; set; }
    public DbSet<SystemSettings> SystemSettings { get; set; }
    public DbSet<Registry> Registries { get; set; }
    public DbSet<RequestLog> RequestLogs { get; set; }
    public DbSet<BlockedIp> BlockedIps { get; set; }
    public DbSet<VersionExcludePattern> VersionExcludePatterns { get; set; }
    public DbSet<ScrapeLog> ScrapeLogs { get; set; }
    public DbSet<BlogPost> BlogPosts { get; set; }
    public DbSet<Contributor> Contributors { get; set; }
    public DbSet<SocialMediaPost> SocialMediaPosts { get; set; }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<PackageSecurityInfo> PackageSecurityInfos { get; set; }
    public DbSet<DependencyAudit> DependencyAudits { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // timestamptz columns that always hold UTC, whatever kind the value handed in has
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
