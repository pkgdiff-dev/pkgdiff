using System.Linq.Expressions;
using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Interfaces;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> GetAllWithIncludeAsync<TProperty>(Expression<Func<T, TProperty>> includeProperty);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null);
    Task<(List<TResult> Items, int Total)> GetPageDescendingAsync<TKey, TResult>(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy,
        Expression<Func<T, TResult>> selector,
        int page,
        int pageSize);
    Task<List<T>> GetTopDescendingWithIncludeAsync<TKey, TProperty>(
        Expression<Func<T, TKey>> orderBy,
        Expression<Func<T, TProperty>> includeProperty,
        int count);

    // Read-only and untracked; run it with IUnitOfWork.ToListAsync or IUnitOfWork.CountAsync
    IQueryable<T> Query();
}
