using System.Linq.Expressions;

using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Pagination partagée par les listes de l'application. Deux cas :
/// <list type="bullet">
/// <item><see cref="Page{T}(IReadOnlyList{T}, int?, int?)"/> : listes déjà calculées ou agrégées
/// en mémoire. Le découpage intervient <b>après</b> l'agrégation, jamais avant.</item>
/// <item><see cref="From{T}"/> puis <see cref="PagedSource{T}.PageAsync{TDto}"/> : listes stockées,
/// déléguées à <c>IRepository.ToPagedAsync</c> (COUNT + OFFSET/FETCH SQL via EF Core).</item>
/// </list>
/// Les deux bornent la page effective à <c>[1, totalPages]</c>, afin qu'une page vidée par une
/// suppression retombe sur la dernière page non vide au lieu d'un écran vide.
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

    /// <summary>Source stockée filtrée et triée, à paginer (éventuellement après une recherche).</summary>
    public static PagedSource<T> From<T>(
        IRepository<T> repository,
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(orderBy);
        return new PagedSource<T>(repository, predicate, orderBy);
    }
}
