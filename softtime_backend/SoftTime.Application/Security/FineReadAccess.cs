using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Application.Security;

/// <summary>
/// Lecture sensible hors Paramètres : l'administrateur passe ; les autres comptes ont besoin de la porte ET de la
/// permission fine (calculées côté serveur, pas lues dans le jeton). Échec fermé si la lecture des droits échoue.
/// </summary>
public static class FineReadAccess
{
    public static async Task EnsureAsync(
        ICurrentUser user, EffectiveRightsService rights, int gate, string permission, CancellationToken cancellationToken = default)
    {
        if (AdminAccess.IsAdmin(user)) return;
        if (user.UserId is not decimal uid)
            throw new ForbiddenOperationException(AuthService.ReadDenied);
        EffectiveRights effective;
        try
        {
            effective = await rights.GetAsync(uid, ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rights.LogReadFailure(uid, ex);
            throw new ForbiddenOperationException(AuthService.ReadDenied);
        }
        if (!effective.HasGate(gate) || !effective.Has(permission))
            throw new ForbiddenOperationException(AuthService.ReadDenied);
    }
}
