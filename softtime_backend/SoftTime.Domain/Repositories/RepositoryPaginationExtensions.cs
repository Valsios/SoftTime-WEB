using System.Linq.Expressions;

using SoftTime.Domain.Models;

namespace SoftTime.Domain.Repositories;

/// <summary>
/// Méthodes d'extension permettant d'appeler ToPagedAsync directement sur toute instance implémentant IRepository&lt;T&gt;.
/// </summary>
public static class RepositoryPaginationExtensions
{
    public static Task<PagedResult<TResult>> ToPagedAsync<T, TResult>(
        this IRepository<T> repo,
        IQueryable<TResult> query,
        PageRequest pagination,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.ToPagedAsync(query, pagination, cancellationToken);
    }

    public static Task<PagedResult<TDto>> ToPagedAsync<T, TDto>(
        this IRepository<T> repo,
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, TDto>> selector,
        PageRequest pagination,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.ToPagedAsync(predicate, orderBy, selector, pagination, cancellationToken);
    }

    public static Task<PagedResult<T>> ToPagedAsync<T>(
        this IRepository<T> repo,
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        PageRequest pagination,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.ToPagedAsync(predicate, orderBy, pagination, cancellationToken);
    }
}
