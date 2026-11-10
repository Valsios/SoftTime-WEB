using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.RequestContext;

/// <summary>Contrat de <c>TenantConnectionService</c>.</summary>
public interface ITenantConnectionService
{
    string SageDb { get; }

    /// <summary>Nom de la base pointeuse de la requête ; refusé (403) si le compte ne peut pas l'utiliser.</summary>
    string PointeuseDb { get; }

    /// <summary>
    /// Utiliser une base SAGE : administrateur, ou porte 1 effective ET base cochée dans « Accès bases » ET lecture effective.
    /// Refus : ForbiddenException (403), jamais 401. Échec fermé si les droits ne peuvent pas être lus.
    /// </summary>
    Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fills the pointeuse tenant from the ACTIVE row (or the first configured one)
    /// when the client did not send X-Pointeuse-Database.
    /// </summary>
    Task ResolvePointeuseAsync(CancellationToken cancellationToken = default);

    Task<BddSage> GetSageRowAsync(CancellationToken cancellationToken = default);

    Task<BddPointeuse> GetPointeuseRowAsync(CancellationToken cancellationToken = default);

    Task<string> GetSageConnectionAsync(CancellationToken cancellationToken = default);

    Task<string> GetPointeuseConnectionAsync(CancellationToken cancellationToken = default);
}
