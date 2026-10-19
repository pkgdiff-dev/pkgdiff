using PkgDiff.Domain.Entities;

namespace PkgDiff.Domain.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IRepository<SearchHistory> SearchHistories { get; }
    IRepository<CachedPackage> CachedPackages { get; }
    IRepository<CachedReleaseNote> CachedReleaseNotes { get; }
    IRepository<SystemSettings> SystemSettings { get; }
    IRepository<Registry> Registries { get; }
    IRepository<RequestLog> RequestLogs { get; }
    IRepository<BlockedIp> BlockedIps { get; }
    IRepository<VersionExcludePattern> VersionExcludePatterns { get; }
    IRepository<ScrapeLog> ScrapeLogs { get; }
    IRepository<BlogPost> BlogPosts { get; }
    IRepository<Contributor> Contributors { get; }
    IRepository<SocialMediaPost> SocialMediaPosts { get; }
    IRepository<PackageSecurityInfo> PackageSecurityInfos { get; }
    IRepository<DependencyAudit> DependencyAudits { get; }

    Task<int> SaveChangesAsync();

    Task<List<TResult>> ToListAsync<TResult>(IQueryable<TResult> query);
    Task<int> CountAsync<TResult>(IQueryable<TResult> query);
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
