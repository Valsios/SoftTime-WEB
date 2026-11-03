using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="RefreshTokenService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
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
