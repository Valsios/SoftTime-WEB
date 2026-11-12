using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Application.Security;

/// <summary>
/// Lecture sensible hors Paramètres : l'administrateur passe ; les autres comptes ont besoin de la porte ET de la
/// permission fine (calculées côté serveur, pas lues dans le jeton). Échec fermé si la lecture des droits échoue.
/// </summary>
public static class FineReadAccess
{
    public static Task EnsureAsync(
        ICurrentUser user, IEffectiveRightsService rights, int gate, string permission, CancellationToken cancellationToken = default)
        => FinePermissionAccess.EnsureAsync(
            user, rights, new FinePermission(gate, permission), ReadPermissions.Denied, cancellationToken);
}
