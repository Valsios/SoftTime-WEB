using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Domain.Repositories;

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<List<T>> ListLatestAsync(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, DateTime?>> orderByDescending,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécute une pagination côté serveur sur une requête IQueryable ordonnée.
    /// Sur EF Core, traduit en SQL (CountAsync + Skip/Take/ToListAsync) sans charger toute la liste.
    /// Fournit une implémentation par défaut en mémoire pour les fakes / tests unitaires.
    /// </summary>
    Task<PagedResult<TResult>> ToPagedAsync<TResult>(
        IQueryable<TResult> query,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var list = query.ToList();
        var total = list.Count;
        var (pageNorm, taille) = PaginationHelper.CalculerPagination(page, pageSize);

        if (total == 0)
        {
            return Task.FromResult(new PagedResult<TResult>(
                Array.Empty<TResult>(),
                0,
                pageNorm,
                taille,
                1));
        }

        var totalPages = PaginationRules.NombreDePages(total, taille);
        if (pageNorm > totalPages)
        {
            return Task.FromResult(new PagedResult<TResult>(
                Array.Empty<TResult>(),
                total,
                pageNorm,
                taille,
                totalPages));
        }

        var items = list
            .Skip(PaginationRules.Decalage(pageNorm, taille))
            .Take(taille)
            .ToList();

        return Task.FromResult(new PagedResult<TResult>(
            items,
            total,
            pageNorm,
            taille,
            totalPages));
    }

    /// <summary>
    /// Exécute une pagination côté serveur avec filtre, tri stable obligatoire et projection vers DTO.
    /// </summary>
    Task<PagedResult<TDto>> ToPagedAsync<TDto>(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, TDto>> selector,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(selector);

        IQueryable<T> query = Query();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        var projected = ordered.Select(selector);
        return ToPagedAsync(projected, page, pageSize, cancellationToken);
    }

    /// <summary>
    /// Exécute une pagination côté serveur avec filtre et tri stable obligatoire sans projection.
    /// </summary>
    Task<PagedResult<T>> ToPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);

        IQueryable<T> query = Query();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        return ToPagedAsync(ordered, page, pageSize, cancellationToken);
    }

    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}
