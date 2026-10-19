using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.Tests.TestDoubles;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public InMemoryRepository<SearchHistory> SearchHistories { get; } = new();
    public InMemoryRepository<CachedPackage> CachedPackages { get; } = new();
    public InMemoryRepository<CachedReleaseNote> CachedReleaseNotes { get; } = new();
    public InMemoryRepository<SystemSettings> SystemSettings { get; } = new();
    public InMemoryRepository<Registry> Registries { get; } = new();
    public InMemoryRepository<RequestLog> RequestLogs { get; } = new();
    public InMemoryRepository<BlockedIp> BlockedIps { get; } = new();
    public InMemoryRepository<VersionExcludePattern> VersionExcludePatterns { get; } = new();
    public InMemoryRepository<ScrapeLog> ScrapeLogs { get; } = new();
    public InMemoryRepository<BlogPost> BlogPosts { get; } = new();
    public InMemoryRepository<Contributor> Contributors { get; } = new();
    public InMemoryRepository<SocialMediaPost> SocialMediaPosts { get; } = new();
    public InMemoryRepository<PackageSecurityInfo> PackageSecurityInfos { get; } = new();
    public InMemoryRepository<DependencyAudit> DependencyAudits { get; } = new();

    public Exception? SaveChangesException { get; set; }

    public int SaveChangesCalls { get; private set; }
    public int BeginTransactionCalls { get; private set; }
    public int CommitTransactionCalls { get; private set; }
    public int RollbackTransactionCalls { get; private set; }

    IRepository<SearchHistory> IUnitOfWork.SearchHistories => SearchHistories;
    IRepository<CachedPackage> IUnitOfWork.CachedPackages => CachedPackages;
    IRepository<CachedReleaseNote> IUnitOfWork.CachedReleaseNotes => CachedReleaseNotes;
    IRepository<SystemSettings> IUnitOfWork.SystemSettings => SystemSettings;
    IRepository<Registry> IUnitOfWork.Registries => Registries;
    IRepository<RequestLog> IUnitOfWork.RequestLogs => RequestLogs;
    IRepository<BlockedIp> IUnitOfWork.BlockedIps => BlockedIps;
    IRepository<VersionExcludePattern> IUnitOfWork.VersionExcludePatterns => VersionExcludePatterns;
    IRepository<ScrapeLog> IUnitOfWork.ScrapeLogs => ScrapeLogs;
    IRepository<BlogPost> IUnitOfWork.BlogPosts => BlogPosts;
    IRepository<Contributor> IUnitOfWork.Contributors => Contributors;
    IRepository<SocialMediaPost> IUnitOfWork.SocialMediaPosts => SocialMediaPosts;
    IRepository<PackageSecurityInfo> IUnitOfWork.PackageSecurityInfos => PackageSecurityInfos;
    IRepository<DependencyAudit> IUnitOfWork.DependencyAudits => DependencyAudits;

    // Simulates a database failure on the Nth SaveChangesAsync call (1-based)
    public int? FailOnSaveChangesCall { get; set; }

    // Simulates a unique-key conflict on the Nth SaveChangesAsync call (1-based)
    public int? DuplicateOnSaveChangesCall { get; set; }

    public Task<List<TResult>> ToListAsync<TResult>(IQueryable<TResult> query) => Task.FromResult(query.ToList());

    public Task<int> CountAsync<TResult>(IQueryable<TResult> query) => Task.FromResult(query.Count());

    public Task<int> SaveChangesAsync()
    {
        SaveChangesCalls++;
        if (SaveChangesCalls == FailOnSaveChangesCall)
        {
            throw new InvalidOperationException("Simulated SaveChanges failure.");
        }

        if (SaveChangesCalls == DuplicateOnSaveChangesCall)
            throw new PkgDiff.Domain.Common.DuplicateKeyException();

        if (SaveChangesException != null)
            throw SaveChangesException;

        return Task.FromResult(0);
    }

    public Task BeginTransactionAsync()
    {
        BeginTransactionCalls++;
        return Task.CompletedTask;
    }

    public Task CommitTransactionAsync()
    {
        CommitTransactionCalls++;
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync()
    {
        RollbackTransactionCalls++;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
