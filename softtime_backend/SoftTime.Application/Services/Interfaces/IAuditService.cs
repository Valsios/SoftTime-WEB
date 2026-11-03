using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="AuditService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
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
    Task<PagedResult<AuditEventDto>> ListEventsPagedAsync(DateTime? from, DateTime? to, string? table, string? action, string? search, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<object>> ListDiscoveredSchemaAsync(CancellationToken ct = default);
}
