using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
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
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.ToPagedAsync(query, page, pageSize, cancellationToken);
    }

    public static Task<PagedResult<TDto>> ToPagedAsync<T, TDto>(
        this IRepository<T> repo,
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, TDto>> selector,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.ToPagedAsync(predicate, orderBy, selector, page, pageSize, cancellationToken);
    }

    public static Task<PagedResult<T>> ToPagedAsync<T>(
        this IRepository<T> repo,
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.ToPagedAsync(predicate, orderBy, page, pageSize, cancellationToken);
    }
}
