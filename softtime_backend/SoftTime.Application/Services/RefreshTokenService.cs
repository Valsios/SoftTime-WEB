using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>Résultat d'une rotation : compte concerné et nouveau jeton (valeur en clair, jamais stockée).</summary>
public sealed record RefreshTokenRotation(decimal UserId, string RefreshToken);

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

    public RefreshTokenService(IUnitOfWork uow, IOptions<AccountSecurityOptions>? options = null)
    {
        _uow = uow;
        _options = options?.Value ?? new AccountSecurityOptions();
    }

    /// <summary>Nouveau jeton pour une connexion (nouvelle famille) ou pour une rotation (famille conservée).</summary>
    public async Task<string> IssueAsync(decimal userId, Guid? family = null, CancellationToken cancellationToken = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = DateTime.UtcNow;
        await _uow.Repository<T_REFRESH_TOKEN>().AddAsync(new T_REFRESH_TOKEN
        {
            IDRESPONSABLE = userId,
            TOKEN_HASH = Hash(token),
            FAMILLE = family ?? Guid.NewGuid(),
            DATE_CREATION = now,
            DATE_EXPIRATION = now.AddDays(_options.RefreshTokenDays)
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

        if (current.DATE_REVOCATION is DateTime revokedAt)
        {
            // Deux onglets qui rafraîchissent en même temps présentent le même jeton : refus simple pendant
            // quelques secondes après la rotation, révocation de toutes les sessions au-delà (vol probable).
            var justRotated = current.REMPLACE_PAR is not null && DateTime.UtcNow - revokedAt < ConcurrentRotationGrace;
            if (justRotated)
                throw new UnauthorizedException(InvalidTokenMessage);
            await RevokeAllAsync(current.IDRESPONSABLE, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException(InvalidTokenMessage);
        }
        if (current.DATE_EXPIRATION <= DateTime.UtcNow)
            throw new UnauthorizedException(InvalidTokenMessage);

        var next = await IssueAsync(current.IDRESPONSABLE, current.FAMILLE, cancellationToken);
        current.DATE_REVOCATION = DateTime.UtcNow;
        current.REMPLACE_PAR = Hash(next);
        _uow.Repository<T_REFRESH_TOKEN>().Update(current);
        return new RefreshTokenRotation(current.IDRESPONSABLE, next);
    }

    /// <summary>Déconnexion : révoque le jeton présenté. Un jeton inconnu est ignoré (rien à révoquer).</summary>
    public async Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var current = await FindAsync(refreshToken, cancellationToken);
        if (current is null || current.DATE_REVOCATION is not null)
            return;
        current.DATE_REVOCATION = DateTime.UtcNow;
        _uow.Repository<T_REFRESH_TOKEN>().Update(current);
    }

    /// <summary>Révoque toutes les sessions actives d'un compte (mot de passe changé, réutilisation détectée).</summary>
    public async Task RevokeAllAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_REFRESH_TOKEN>();
        var now = DateTime.UtcNow;
        foreach (var token in await repo.ListAsync(t => t.IDRESPONSABLE == userId && t.DATE_REVOCATION == null, cancellationToken))
        {
            token.DATE_REVOCATION = now;
            repo.Update(token);
        }
    }

    /// <summary>Compte supprimé : ses jetons partent avec lui, dans la même transaction.</summary>
    public async Task RemoveAllAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_REFRESH_TOKEN>();
        repo.RemoveRange(await repo.ListAsync(t => t.IDRESPONSABLE == userId, cancellationToken));
    }

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private async Task<T_REFRESH_TOKEN?> FindAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;
        var hash = Hash(refreshToken.Trim());
        return (await _uow.Repository<T_REFRESH_TOKEN>().ListAsync(t => t.TOKEN_HASH == hash, cancellationToken)).FirstOrDefault();
    }
}
