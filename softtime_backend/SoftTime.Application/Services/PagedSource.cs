using System.Linq.Expressions;

using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>Source stockée paginée côté serveur : filtre métier, tri stable et recherche éventuelle.</summary>
public sealed class PagedSource<T>
    where T : class
{
    private readonly IRepository<T> _repository;
    private readonly Expression<Func<T, bool>> _predicate;
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>> _orderBy;

    internal PagedSource(
        IRepository<T> repository,
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy)
    {
        _repository = repository;
        _predicate = predicate;
        _orderBy = orderBy;
    }

    /// <summary>
    /// Recherche serveur : combinée au filtre métier avant la pagination SQL, afin que le total et
    /// les pages portent sur toutes les données.
    /// </summary>
    public PagedSource<T> WithSearch(string? search, Expression<Func<T, string?>>[] fields)
    {
        var searchPredicate = SearchExpression.For(search, fields);
        return searchPredicate is null
            ? this
            : new PagedSource<T>(_repository, SearchExpression.AndAlso(_predicate, searchPredicate), _orderBy);
    }

    /// <summary>
    /// Page demandée, bornée : le contrat <c>ToPagedAsync</c> renvoie la page demandée même hors
    /// bornes ; une page vidée (dernière ligne supprimée, page profonde) est rejouée sur la dernière
    /// page réelle — une seule requête supplémentaire, uniquement dans ce cas.
    /// </summary>
    public async Task<PagedResult<TDto>> PageAsync<TDto>(PageRequest pagination, Func<T, TDto> map, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = await _repository.ToPagedAsync(_predicate, _orderBy, pagination, ct);
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await _repository.ToPagedAsync(_predicate, _orderBy, new PageRequest(result.TotalPages, result.PageSize), ct);

        return new PagedResult<TDto>(
            result.Items.Select(map).ToList(),
            result.Total,
            PaginationRules.PageEffective(result.Total, result.Page),
            result.PageSize,
            result.TotalPages);
    }
}
