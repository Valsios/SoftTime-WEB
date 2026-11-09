using SoftTime.Application.Security;

namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Lit en base ce qu'il faut pour calculer les droits effectifs d'un compte (étape 3B) et les met en cache.
/// Les erreurs de lecture remontent à l'appelant, qui doit REFUSER l'accès (échec fermé).
/// </summary>
public interface IEffectiveRightsService
{
    void Invalidate(decimal userId);

    void InvalidateAll();

    Task<EffectiveRights> GetAsync(decimal userId, bool fresh = false, CancellationToken ct = default);

    /// <summary>
    /// Ajustements personnels réels d'un compte par rapport à son rôle actuel : droits ajoutés (« +CODE ») et retirés (« -CODE »).
    /// C'est ce qu'un changement de rôle effacerait ; lecture seule.
    /// </summary>
    Task<IReadOnlyList<string>> AdjustmentsAsync(decimal userId, CancellationToken ct = default);

    /// <summary>
    /// Droits effectifs d'un rôle pris seul (sans ajustement de compte), tels que le calculateur les accorde :
    /// lignes détaillées du rôle, ou droits par défaut de ses portes SANS la configuration des bases.
    /// Sert à afficher les droits d'un rôle sans lignes : l'écran montre exactement ce que le serveur accorde.
    /// </summary>
    Task<IReadOnlyList<string>> RoleDefaultPermissionsAsync(decimal roleId, CancellationToken ct = default);

    /// <summary>Journal sans donnée sensible : identifiant du compte et type d'erreur seulement.</summary>
    void LogReadFailure(decimal userId, Exception ex);
}
