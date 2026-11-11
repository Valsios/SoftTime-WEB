using SoftTime.Application.Security;

namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Lit en base ce qu'il faut pour calculer les droits effectifs d'un compte et les met en cache.
/// Les erreurs de lecture remontent à l'appelant, qui doit REFUSER l'accès (échec fermé).
/// </summary>
public interface IEffectiveRightsService
{
    void Invalidate(decimal userId);

    void InvalidateAll();

    Task<EffectiveRights> GetAsync(decimal userId, bool fresh = false, CancellationToken ct = default);

    /// <summary>Droits que le rôle actuel du compte lui accorde seul, sans ses ajouts ni ses retraits (jamais en cache).</summary>
    Task<EffectiveRights> RoleOnlyAsync(decimal userId, CancellationToken ct = default);

    /// <summary>
    /// Ajustements personnels réels d'un compte par rapport à son rôle actuel : droits ajoutés (« +CODE ») et retirés (« -CODE »).
    /// C'est ce qu'un changement de rôle effacerait ; lecture seule.
    /// </summary>
    Task<IReadOnlyList<string>> AdjustmentsAsync(decimal userId, CancellationToken ct = default);

    /// <summary>Journal sans donnée sensible : identifiant du compte et type d'erreur seulement.</summary>
    void LogReadFailure(decimal userId, Exception ex);
}
