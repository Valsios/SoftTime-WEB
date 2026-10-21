using System.Linq.Expressions;

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
    /// Lit une page en laissant le fournisseur traduire le filtre, l'ordre et le découpage en SQL
    /// (OFFSET/FETCH). À utiliser pour les listes massives : contrairement à <see cref="ListAsync"/>,
    /// seules les lignes de la page sont matérialisées. Lecture seule : aucune entité n'est suivie
    /// par le change tracker.
    /// </summary>
    Task<List<T>> ListPagedAsync(
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Compte les lignes correspondant au filtre, sans les matérialiser.</summary>
    Task<int> CountAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}
