using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Infrastructure.Services;

public sealed class ParametersAccessGuard : IParametersAccessGuard
{
    private readonly ICurrentUser _currentUser;
    private readonly IEffectiveRightsService _effectiveRights;

    public ParametersAccessGuard(
        ICurrentUser currentUser,
        IEffectiveRightsService effectiveRights)
    {
        _currentUser = currentUser;
        _effectiveRights = effectiveRights;
    }

    /// <summary>
    /// Consultation sensible : administrateur, ou porte 2 ET l'une des permissions fines, lues dans les droits effectifs
    /// (pas dans le jeton). Échec fermé si la lecture des droits échoue.
    /// </summary>
    public async Task EnsureCanReadAsync(string[] anyOf, CancellationToken cancellationToken = default)
    {
        if (AdminAccess.IsAdmin(_currentUser)) return;
        if (_currentUser.UserId is not decimal uid)
            throw new ForbiddenException(ReadPermissions.Denied);
        EffectiveRights rights;
        try
        {
            rights = await _effectiveRights.GetAsync(uid, ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _effectiveRights.LogReadFailure(uid, ex);
            throw new ForbiddenException(ReadPermissions.Denied);
        }
        if (!rights.HasGate(Rights.Parameters) || !anyOf.Any(rights.Has))
            throw new ForbiddenException(ReadPermissions.Denied);
    }

    /// <summary>Opérations sur les autres comptes : réservées aux administrateurs (rôle 1) (rôle 1 uniquement).</summary>
    public void RequireAdministrator()
    {
        AdminAccess.Require(_currentUser);
    }
}
