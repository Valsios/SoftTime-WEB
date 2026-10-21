using System.Linq.Expressions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Pagination partagée par les listes de l'application. Deux cas :
/// <list type="bullet">
/// <item><see cref="Page{T}(IReadOnlyList{T}, int?, int?)"/> : listes déjà calculées ou agrégées
/// en mémoire. Le découpage intervient <b>après</b> l'agrégation, jamais avant.</item>
/// <item><see cref="PageAsync{T,TDto}"/> : listes stockées. Le filtre, l'ordre et le
/// <c>OFFSET/FETCH</c> sont délégués au fournisseur, et seules les lignes de la page sont
/// matérialisées.</item>
/// </list>
/// Dans les deux cas la page demandée est bornée à <c>[1, totalPages]</c> et la page effective est
/// renvoyée dans <see cref="PagedResult{T}.Page"/>, afin qu'une page vidée par une suppression
/// retombe sur la dernière page non vide au lieu d'un écran vide.
/// </summary>
public static class PagedLists
{
    /// <summary>Page une liste déjà ordonnée (calculée ou agrégée en mémoire).</summary>
    public static PagedResult<T> Page<T>(IReadOnlyList<T> orderedSource, int? page, int? pageSize)
    {
        var (pageDemandee, taille) = PaginationRules.Normaliser(page, pageSize);
        var total = orderedSource.Count;
        var totalPages = PaginationRules.NombreDePages(total, taille);
        var pageEffective = Math.Min(pageDemandee, totalPages);
        var items = total == 0
            ? (IReadOnlyList<T>)Array.Empty<T>()
            : orderedSource
                .Skip(PaginationRules.Decalage(pageEffective, taille))
                .Take(taille)
                .ToList();
        return new PagedResult<T>(items, total, pageEffective, taille, totalPages);
    }

    /// <summary>
    /// Page une source stockée : <c>COUNT(*)</c> puis <c>ORDER BY … OFFSET … FETCH</c> côté
    /// fournisseur. <paramref name="predicate"/> doit être identique à celui de la liste non paginée
    /// correspondante pour que <c>Total</c> et <c>Items</c> restent cohérents.
    /// </summary>
    public static async Task<PagedResult<TDto>> PageAsync<T, TDto>(
        IRepository<T> repository,
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        int? page,
        int? pageSize,
        Func<T, TDto> map,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(map);

        var (pageDemandee, taille) = PaginationRules.Normaliser(page, pageSize);
        var total = await repository.CountAsync(predicate, ct);
        var totalPages = PaginationRules.NombreDePages(total, taille);
        var pageEffective = Math.Min(pageDemandee, totalPages);
        var entities = total == 0
            ? new List<T>()
            : await repository.ListPagedAsync(
                predicate,
                orderBy,
                PaginationRules.Decalage(pageEffective, taille),
                taille,
                ct);
        return new PagedResult<TDto>(
            entities.Select(map).ToList(),
            total,
            pageEffective,
            taille,
            totalPages);
    }
}
