using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Common;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.Infrastructure.Repositories;

public class Repository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(new object[] { id }, WorkCancellation.Token);
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync(WorkCancellation.Token);
    }

    public async Task<IEnumerable<T>> GetAllWithIncludeAsync<TProperty>(Expression<Func<T, TProperty>> includeProperty)
    {
        return await _dbSet.Include(includeProperty).ToListAsync(WorkCancellation.Token);
    }

    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.Where(predicate).ToListAsync(WorkCancellation.Token);
    }

    public async Task<T> AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
        return entity;
    }

    public Task UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    // One DELETE statement, executed at once: it is not part of the next SaveChangesAsync
    public async Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.Where(predicate).ExecuteDeleteAsync();
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        return predicate == null
            ? await _dbSet.CountAsync(WorkCancellation.Token)
            : await _dbSet.CountAsync(predicate, WorkCancellation.Token);
    }

    public async Task<(List<TResult> Items, int Total)> GetPageDescendingAsync<TKey, TResult>(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy,
        Expression<Func<T, TResult>> selector,
        int page,
        int pageSize)
    {
        var filtered = _dbSet.AsNoTracking().Where(predicate);
        var total = await filtered.CountAsync(WorkCancellation.Token);

        // Id breaks ties, so a row never moves between two pages of the same request series
        var items = await filtered
            .OrderByDescending(orderBy)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToListAsync(WorkCancellation.Token);

        return (items, total);
    }

    public async Task<List<T>> GetTopDescendingWithIncludeAsync<TKey, TProperty>(
        Expression<Func<T, TKey>> orderBy,
        Expression<Func<T, TProperty>> includeProperty,
        int count)
    {
        return await _dbSet.AsNoTracking()
            .Include(includeProperty)
            .OrderByDescending(orderBy)
            .ThenByDescending(e => e.Id)
            .Take(count)
            .ToListAsync(WorkCancellation.Token);
    }

    public IQueryable<T> Query()
    {
        return _dbSet.AsNoTracking();
    }
}
