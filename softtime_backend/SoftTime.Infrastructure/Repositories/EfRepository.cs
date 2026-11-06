using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Infrastructure.Repositories;

public class EfRepository<T> : IRepository<T> where T : class
{
    private readonly SoftTimeDbContext _db;
    private readonly DbSet<T> _set;

    public EfRepository(SoftTimeDbContext db)
    {
        _db = db;
        _set = db.Set<T>();
    }

    public IQueryable<T> Query() => _set.AsQueryable();

    public IQueryable<T> QueryReadOnly() => _set.AsNoTracking();

    public async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => await _set.FindAsync(new[] { id }, cancellationToken);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => _set.Where(predicate).ToListAsync(cancellationToken);

    public Task<List<T>> ListReadOnlyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => _set.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);

    public Task<List<T>> ListLatestAsync(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, DateTime?>> orderByDescending,
        int take,
        CancellationToken cancellationToken = default)
        => _set.Where(predicate).OrderByDescending(orderByDescending).Take(take).ToListAsync(cancellationToken);

    public Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken cancellationToken = default)
        => EntityFrameworkQueryableExtensions.CountAsync(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    public Task<List<TResult>> ToListAsync<TResult>(IQueryable<TResult> query, CancellationToken cancellationToken = default)
        => EntityFrameworkQueryableExtensions.ToListAsync(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    public async Task<PagedResult<TResult>> ToPagedAsync<TResult>(
        IQueryable<TResult> query,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var total = await query.CountAsync(cancellationToken);
        var (pageNorm, taille) = PaginationRules.Normaliser(page, pageSize);

        if (total == 0)
        {
            return new PagedResult<TResult>(
                Array.Empty<TResult>(),
                0,
                pageNorm,
                taille,
                1);
        }

        var totalPages = PaginationRules.NombreDePages(total, taille);
        if (pageNorm > totalPages)
        {
            return new PagedResult<TResult>(
                Array.Empty<TResult>(),
                total,
                pageNorm,
                taille,
                totalPages);
        }

        var items = await query
            .Skip(PaginationRules.Decalage(pageNorm, taille))
            .Take(taille)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(
            items,
            total,
            pageNorm,
            taille,
            totalPages);
    }

    public async Task<PagedResult<TDto>> ToPagedAsync<TDto>(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, TDto>> selector,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(selector);

        IQueryable<T> query = _set.AsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        var projected = ordered.Select(selector);
        return await ToPagedAsync(projected, page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<T>> ToPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);

        IQueryable<T> query = _set.AsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        return await ToPagedAsync(ordered, page, pageSize, cancellationToken);
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await _set.AddAsync(entity, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        => await _set.AddRangeAsync(entities, cancellationToken);

    public void Update(T entity) => _set.Update(entity);

    public void Remove(T entity) => _set.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) => _set.RemoveRange(entities);
}
