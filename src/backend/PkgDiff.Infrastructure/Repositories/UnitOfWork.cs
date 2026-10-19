using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Common;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
        SearchHistories = new Repository<SearchHistory>(context);
        CachedPackages = new Repository<CachedPackage>(context);
        CachedReleaseNotes = new Repository<CachedReleaseNote>(context);
        SystemSettings = new Repository<SystemSettings>(context);
        Registries = new Repository<Registry>(context);
        RequestLogs = new Repository<RequestLog>(context);
        BlockedIps = new Repository<BlockedIp>(context);
        VersionExcludePatterns = new Repository<VersionExcludePattern>(context);
        ScrapeLogs = new Repository<ScrapeLog>(context);
        BlogPosts = new Repository<BlogPost>(context);
        Contributors = new Repository<Contributor>(context);
        SocialMediaPosts = new Repository<SocialMediaPost>(context);
        PackageSecurityInfos = new Repository<PackageSecurityInfo>(context);
        DependencyAudits = new Repository<DependencyAudit>(context);
    }

    public IRepository<SearchHistory> SearchHistories { get; }
    public IRepository<CachedPackage> CachedPackages { get; }
    public IRepository<CachedReleaseNote> CachedReleaseNotes { get; }
    public IRepository<SystemSettings> SystemSettings { get; }
    public IRepository<Registry> Registries { get; }
    public IRepository<RequestLog> RequestLogs { get; }
    public IRepository<BlockedIp> BlockedIps { get; }
    public IRepository<VersionExcludePattern> VersionExcludePatterns { get; }
    public IRepository<ScrapeLog> ScrapeLogs { get; }
    public IRepository<BlogPost> BlogPosts { get; }
    public IRepository<Contributor> Contributors { get; }
    public IRepository<SocialMediaPost> SocialMediaPosts { get; }
    public IRepository<PackageSecurityInfo> PackageSecurityInfos { get; }
    public IRepository<DependencyAudit> DependencyAudits { get; }

    public Task<List<TResult>> ToListAsync<TResult>(IQueryable<TResult> query) =>
        EntityFrameworkQueryableExtensions.ToListAsync(query, WorkCancellation.Token);

    public Task<int> CountAsync<TResult>(IQueryable<TResult> query) =>
        EntityFrameworkQueryableExtensions.CountAsync(query, WorkCancellation.Token);

    public async Task<int> SaveChangesAsync()
    {
        try
        {
            return await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The whole batch was rolled back. The rows it tried to insert are forgotten, or the next
            // save of this context would send them again and fail the same way.
            foreach (var entry in _context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;

            throw new DuplicateKeyException(ex);
        }
    }

    public async Task BeginTransactionAsync()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
