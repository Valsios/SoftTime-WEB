using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Authentication;

/// <summary>
/// Jetons de rafraîchissement : émission, rotation à chaque usage, révocation, et détection de réutilisation
/// (un jeton déjà remplacé qui revient révoque toutes les sessions du compte). Seule l'empreinte SHA-256 est stockée.
/// Les écritures sont mises en attente : l'appelant valide avec <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    public const string InvalidTokenMessage = "Session expirée. Veuillez vous reconnecter.";
    private const int TokenBytes = 64;
    private static readonly TimeSpan ConcurrentRotationGrace = TimeSpan.FromSeconds(30);

    private readonly IUnitOfWork _uow;
    private readonly AccountSecurityOptions _options;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(IUnitOfWork uow, IOptions<AccountSecurityOptions>? options = null, ILogger<RefreshTokenService>? logger = null)
    {
        _logger = logger ?? NullLogger<RefreshTokenService>.Instance;
        _uow = uow;
        _options = options?.Value ?? new AccountSecurityOptions();
    }

    /// <summary>Nouveau jeton pour une connexion (nouvelle famille) ou pour une rotation (famille conservée).</summary>
    public async Task<string> IssueAsync(decimal userId, Guid? family = null, CancellationToken cancellationToken = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = DateTime.UtcNow;
        await _uow.Repository<RefreshTokenRecord>().AddAsync(new RefreshTokenRecord
        {
            IdResponsable = userId,
            TokenHash = Hash(token),
            Famille = family ?? Guid.NewGuid(),
            DateCreation = now,
            DateExpiration = now.AddDays(_options.RefreshTokenDays)
        }, cancellationToken);
        return token;
    }

    /// <summary>
    /// Échange un jeton valide contre un nouveau (l'ancien est révoqué). Jeton inconnu ou expiré : refus.
    /// Jeton déjà révoqué : tentative de réutilisation, toutes les sessions du compte sont révoquées puis refus.
    /// </summary>
    public async Task<RefreshTokenRotation> RotateAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var current = await FindAsync(refreshToken, cancellationToken)
            ?? throw new UnauthorizedException(InvalidTokenMessage);

        if (current.DateRevocation is DateTime revokedAt)
        {
            // Deux onglets qui rafraîchissent en même temps présentent le même jeton : refus simple pendant
            // quelques secondes après la rotation, révocation de toutes les sessions au-delà (vol probable).
            var justRotated = current.RemplacePar is not null && DateTime.UtcNow - revokedAt < ConcurrentRotationGrace;
            if (justRotated)
                throw new UnauthorizedException(InvalidTokenMessage);
            _logger.LogWarning(
                "Réutilisation d'un jeton de rafraîchissement déjà remplacé (compte {UserId}, famille {Family}) : vol probable, toutes les sessions du compte sont fermées.",
                current.IdResponsable, current.Famille);
            await RevokeAllAsync(current.IdResponsable, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException(InvalidTokenMessage);
        }
        if (current.DateExpiration <= DateTime.UtcNow)
            throw new UnauthorizedException(InvalidTokenMessage);

        var next = await IssueAsync(current.IdResponsable, current.Famille, cancellationToken);
        current.DateRevocation = DateTime.UtcNow;
        current.RemplacePar = Hash(next);
        _uow.Repository<RefreshTokenRecord>().Update(current);
        return new RefreshTokenRotation(current.IdResponsable, next);
    }

    /// <summary>Déconnexion : révoque le jeton présenté. Un jeton inconnu est ignoré (rien à révoquer).</summary>
    public async Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var current = await FindAsync(refreshToken, cancellationToken);
        if (current is null || current.DateRevocation is not null)
            return;
        current.DateRevocation = DateTime.UtcNow;
        _uow.Repository<RefreshTokenRecord>().Update(current);
    }

    /// <summary>Révoque toutes les sessions actives d'un compte (mot de passe changé, réutilisation détectée).</summary>
    public async Task RevokeAllAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<RefreshTokenRecord>();
        var now = DateTime.UtcNow;
        foreach (var token in await repo.ListAsync(t => t.IdResponsable == userId && t.DateRevocation == null, cancellationToken))
        {
            token.DateRevocation = now;
            repo.Update(token);
        }
    }

    /// <summary>Compte supprimé : ses jetons partent avec lui, dans la même transaction.</summary>
    public async Task RemoveAllAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<RefreshTokenRecord>();
        repo.RemoveRange(await repo.ListAsync(t => t.IdResponsable == userId, cancellationToken));
    }

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private async Task<RefreshTokenRecord?> FindAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;
        var hash = Hash(refreshToken.Trim());
        return (await _uow.Repository<RefreshTokenRecord>().ListAsync(t => t.TokenHash == hash, cancellationToken)).FirstOrDefault();
    }
}
