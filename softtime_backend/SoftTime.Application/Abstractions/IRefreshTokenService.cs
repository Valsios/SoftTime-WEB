using SoftTime.Application.Services;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Jetons de rafraîchissement : émission, rotation à chaque usage, révocation, et détection de réutilisation
/// (un jeton déjà remplacé qui revient révoque toutes les sessions du compte). Seule l'empreinte SHA-256 est stockée.
/// Les écritures sont mises en attente : l'appelant valide avec <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Nouveau jeton pour une connexion (nouvelle famille) ou pour une rotation (famille conservée).</summary>
    Task<string> IssueAsync(decimal userId, Guid? family = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Échange un jeton valide contre un nouveau (l'ancien est révoqué). Jeton inconnu ou expiré : refus.
    /// Jeton déjà révoqué : tentative de réutilisation, toutes les sessions du compte sont révoquées puis refus.
    /// </summary>
    Task<RefreshTokenRotation> RotateAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Déconnexion : révoque le jeton présenté. Un jeton inconnu est ignoré (rien à révoquer).</summary>
    Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Révoque toutes les sessions actives d'un compte (mot de passe changé, réutilisation détectée).</summary>
    Task RevokeAllAsync(decimal userId, CancellationToken cancellationToken = default);

    /// <summary>Compte supprimé : ses jetons partent avec lui, dans la même transaction.</summary>
    Task RemoveAllAsync(decimal userId, CancellationToken cancellationToken = default);
}
