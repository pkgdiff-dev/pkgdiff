using System.Linq.Expressions;
using PkgDiff.Domain.Common;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.Tests.TestDoubles;

public sealed class InMemoryRepository<T> : IRepository<T> where T : BaseEntity
{
    private readonly List<T> _items = new();

    public IReadOnlyList<T> Items => _items;

    public Task<T?> GetByIdAsync(int id) => Task.FromResult(_items.FirstOrDefault(e => e.Id == id));

    public Task<IEnumerable<T>> GetAllAsync() => Task.FromResult<IEnumerable<T>>(_items.ToList());

    public Task<IEnumerable<T>> GetAllWithIncludeAsync<TProperty>(Expression<Func<T, TProperty>> includeProperty) =>
        GetAllAsync();

    public Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        Task.FromResult<IEnumerable<T>>(_items.Where(predicate.Compile()).ToList());

    public Task<T> AddAsync(T entity)
    {
        if (entity.Id == 0)
            entity.Id = _items.Count == 0 ? 1 : _items.Max(e => e.Id) + 1;

        _items.Add(entity);
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(T entity) => Task.CompletedTask;

    public Task DeleteAsync(T entity)
    {
        _items.Remove(entity);
        return Task.CompletedTask;
    }

    public Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate) =>
        Task.FromResult(_items.RemoveAll(new Predicate<T>(predicate.Compile())));

    public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null) =>
        Task.FromResult(predicate is null ? _items.Count : _items.Count(predicate.Compile()));

    public Task<(List<TResult> Items, int Total)> GetPageDescendingAsync<TKey, TResult>(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy,
        Expression<Func<T, TResult>> selector,
        int page,
        int pageSize)
    {
        var filtered = _items.AsQueryable().Where(predicate);
        var items = filtered
            .OrderByDescending(orderBy)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToList();

        return Task.FromResult((items, filtered.Count()));
    }

    public Task<List<T>> GetTopDescendingWithIncludeAsync<TKey, TProperty>(
        Expression<Func<T, TKey>> orderBy,
        Expression<Func<T, TProperty>> includeProperty,
        int count) =>
        Task.FromResult(_items.AsQueryable().OrderByDescending(orderBy).ThenByDescending(e => e.Id).Take(count).ToList());

    public IQueryable<T> Query() => _items.AsQueryable();
}
