using System.Linq.Expressions;

using SoftTime.Domain.Models;

namespace SoftTime.Domain.Repositories;

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();

    /// <summary>Requête en lecture seule (AsNoTracking).</summary>
    IQueryable<T> QueryReadOnly() => Query();

    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : entités non suivies (AsNoTracking), à ne pas modifier puis sauvegarder.</summary>
    Task<List<T>> ListReadOnlyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ListAsync(predicate, cancellationToken);

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
        PageRequest pagination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = query.ToList();
        var fenetre = PageWindow.Pour(list.Count, pagination.Page, pagination.PageSize);
        IReadOnlyList<TResult> items = fenetre.EstVide
            ? Array.Empty<TResult>()
            : list.Skip(fenetre.Decalage).Take(fenetre.Taille).ToList();
        return Task.FromResult(fenetre.Resultat(items));
    }

    /// <summary>
    /// Exécute une pagination côté serveur avec filtre, tri stable obligatoire et projection vers DTO.
    /// </summary>
    Task<PagedResult<TDto>> ToPagedAsync<TDto>(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, TDto>> selector,
        PageRequest pagination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(selector);

        IQueryable<T> query = Query();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        var projected = ordered.Select(selector);
        return ToPagedAsync(projected, pagination, cancellationToken);
    }

    /// <summary>
    /// Exécute une pagination côté serveur avec filtre et tri stable obligatoire sans projection.
    /// </summary>
    Task<PagedResult<T>> ToPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        PageRequest pagination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);

        IQueryable<T> query = Query();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        return ToPagedAsync(ordered, pagination, cancellationToken);
    }

    /// <summary>
    /// Compte les lignes d'une requête construite sur <see cref="Query"/> (COUNT SQL sur EF Core).
    /// Implémentation par défaut en mémoire pour les fakes / tests unitaires.
    /// </summary>
    Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult(query.Count());
    }

    /// <summary>
    /// Matérialise une requête construite sur <see cref="Query"/> (déjà filtrée, triée et bornée par l'appelant).
    /// Implémentation par défaut en mémoire pour les fakes / tests unitaires.
    /// </summary>
    Task<List<TResult>> ToListAsync<TResult>(IQueryable<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult(query.ToList());
    }

    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}
