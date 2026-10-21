using System.Linq.Expressions;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Pagination partagée par les listes de l'application. Deux cas :
/// <list type="bullet">
/// <item><see cref="Page{T}(IReadOnlyList{T}, int?, int?)"/> : listes déjà calculées ou agrégées
/// en mémoire. Le découpage intervient <b>après</b> l'agrégation, jamais avant.</item>
/// <item><see cref="PageAsync{T,TDto}"/> : listes stockées, déléguées à
/// <see cref="IRepository{T}.ToPagedAsync{TResult}"/> (COUNT + OFFSET/FETCH SQL via EF Core).</item>
/// </list>
/// Les deux méthodes bornent la page effective à <c>[1, totalPages]</c>, afin qu'une page vidée par
/// une suppression retombe sur la dernière page non vide au lieu d'un écran vide.
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
    /// Page une source stockée via le noyau de pagination de la fondation. Le contrat
    /// <c>ToPagedAsync</c> renvoie la page demandée, même hors bornes ; on la borne ici pour
    /// préserver le comportement attendu par les grilles (page vidée après suppression).
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

        var result = await repository.ToPagedAsync(predicate, orderBy, page, pageSize, ct);

        // Une page hors bornes (dernière ligne supprimée, page profonde) est rejouée sur la
        // dernière page réelle : une seule requête supplémentaire, uniquement dans ce cas.
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await repository.ToPagedAsync(predicate, orderBy, result.TotalPages, result.PageSize, ct);

        var pageEffective = result.Total == 0 ? 1 : result.Page;
        return new PagedResult<TDto>(
            result.Items.Select(map).ToList(),
            result.Total,
            pageEffective,
            result.PageSize,
            result.TotalPages);
    }
}
