using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;

namespace SoftTime.Application.Security;

/// <summary>
/// Contrôle serveur d'une permission fine : l'administrateur passe ; les autres comptes ont besoin de la porte ET de la
/// permission dans leurs droits effectifs (calculés en base, pas lus dans le jeton). Échec fermé si la lecture échoue.
/// <para>
/// Règle héritée, identique au frontend (access.logic.ts) : un compte sans aucune permission fine enregistrée (ni sur
/// le rôle ni sur le compte) reçoit les droits par défaut de ses portes, donc toutes les permissions de chaque porte.
/// </para>
/// </summary>
public static class FinePermissionAccess
{
    public const string ActionDenied = "Vous n'avez pas la permission requise pour cette action.";

    public static async Task EnsureAsync(
        ICurrentUser user, IEffectiveRightsService rights, FinePermission required, string deniedMessage,
        CancellationToken cancellationToken = default)
    {
        if (AdminAccess.IsAdmin(user)) return;
        if (user.UserId is not decimal uid)
            throw new ForbiddenException(deniedMessage);
        EffectiveRights effective;
        try
        {
            effective = await rights.GetAsync(uid, ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rights.LogReadFailure(uid, ex);
            throw new ForbiddenException(deniedMessage);
        }
        if (!effective.HasGate(required.Gate) || !effective.Has(required.Code))
            throw new ForbiddenException(deniedMessage);
    }
}
