using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Lecture des listes de bases SAGE et pointeuse : réservée aux portes 1 (Bases) et 2 (Paramètres), qui s'en servent
/// dans leurs écrans et dans la barre du haut. Les autres comptes reçoivent une liste vide plutôt qu'une erreur.
/// </summary>
public static class DatabaseListAccess
{
    public static bool PeutLire(ICurrentUser user) => user.HasRight(Droit.Databases) || user.HasRight(Droit.Parameters);

    public static PagedResult<TItem> PageVide<TItem>(int? page, int? pageSize) =>
        new(Array.Empty<TItem>(), 0, 1, PaginationRules.Normaliser(page, pageSize).Taille, 1);
}
