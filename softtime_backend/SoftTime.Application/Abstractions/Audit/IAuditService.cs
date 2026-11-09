using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions.Audit;

/// <summary>Contrat de <c>AuditService</c>.</summary>
public interface IAuditService
{
    IReadOnlyList<AuditTypeOption> ListAllowedTypes();

    Task<IReadOnlyList<AuditConfigDto>> ListConfigAsync(CancellationToken ct = default);

    Task SaveConfigAsync(AuditConfigDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<AuditEventDto>> ListEventsAsync(DateTime? from, DateTime? to, string? table, string? action, CancellationToken ct = default);

    /// <summary>Configuration d'audit paginée (petite table, mais contrat identique aux autres grilles).</summary>
    Task<PagedResult<AuditConfigDto>> ListConfigPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Journal d'audit paginé. Contrairement à <see cref="ListEventsAsync"/> (plafond 500 pour
    /// l'export), le total et la page sont calculés en SQL sur le filtre complet.
    /// </summary>
    Task<PagedResult<AuditEventDto>> ListEventsPagedAsync(AuditEventsQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<object>> ListDiscoveredSchemaAsync(CancellationToken ct = default);
}
