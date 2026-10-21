using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class AuditService
{
    private readonly IUnitOfWork _uow;

    public AuditService(IUnitOfWork uow) => _uow = uow;

    public IReadOnlyList<AuditTypeOption> ListAllowedTypes() => AuditConfigurationRules.AllowedTypeOptions;

    public async Task<IReadOnlyList<AuditConfigDto>> ListConfigAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<suivi_TBLCLN>().ListAsync(_ => true, ct);
        return list.Select(c => new AuditConfigDto(c.Id, c.TABLES, c.COLONNES, c.TYPES, c.ETATS, c.BASES)).ToList();
    }

    public async Task SaveConfigAsync(AuditConfigDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<suivi_TBLCLN>();
        if (dto.Id == 0)
        {
            AuditConfigurationRules.Valider(dto.Type);
            var typeCanonique = AuditConfigurationRules.Normalize(dto.Type!);

            await repo.AddAsync(new suivi_TBLCLN
            {
                TABLES = dto.Table,
                COLONNES = dto.Column,
                TYPES = typeCanonique,
                ETATS = dto.Enabled,
                BASES = dto.Base
            }, ct);
        }
        else
        {
            var e = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            e.TABLES = dto.Table;
            e.COLONNES = dto.Column;

            // Si le type a été modifié par rapport à l'existant (comparaison insensible)
            var typeInchange = string.Equals(e.TYPES, dto.Type, StringComparison.OrdinalIgnoreCase);
            if (!typeInchange)
            {
                AuditConfigurationRules.Valider(dto.Type);
                e.TYPES = AuditConfigurationRules.Normalize(dto.Type!);
            }
            // Si le type est inchangé, on préserve la valeur existante en base sans la réécrire ni la normaliser

            e.ETATS = dto.Enabled;
            e.BASES = dto.Base;
            repo.Update(e);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEventDto>> ListEventsAsync(DateTime? from, DateTime? to, string? table, string? action, CancellationToken ct = default)
    {
        var fromDate = from ?? DateTime.Today.AddDays(-30);
        var toDate = to ?? DateTime.Today.AddDays(1);
        var list = await _uow.Repository<suivi_SUIVI>().ListLatestAsync(
            e => e._DATETIMES >= fromDate
                 && e._DATETIMES <= toDate
                 && (string.IsNullOrEmpty(table) || e._TABLES == table)
                 && (string.IsNullOrEmpty(action) || e._ACTIONS == action),
            e => e._DATETIMES,
            take: 500,
            ct);
        return list
            .Select(e => new AuditEventDto(e.Id, e._TABLES, e._COLONNES, e._USERS, e._ACTIONS, e._DATETIMES, e._NOUVEAUX, e._ANCIENS, e._BASES))
            .ToList();
    }

    /// <summary>Configuration d'audit paginée (petite table, mais contrat identique aux autres grilles).</summary>
    public async Task<PagedResult<AuditConfigDto>> ListConfigPagedAsync(
        int? page, int? pageSize, CancellationToken ct = default)
        => await PagedLists.PageAsync(
            _uow.Repository<suivi_TBLCLN>(),
            _ => true,
            q => q.OrderBy(c => c.Id),
            page,
            pageSize,
            c => new AuditConfigDto(c.Id, c.TABLES, c.COLONNES, c.TYPES, c.ETATS, c.BASES),
            ct);

    /// <summary>
    /// Journal d'audit paginé. Contrairement à <see cref="ListEventsAsync"/> (plafond 500 pour
    /// l'export), le total et la page sont calculés en SQL sur le filtre complet.
    /// </summary>
    public async Task<PagedResult<AuditEventDto>> ListEventsPagedAsync(
        DateTime? from, DateTime? to, string? table, string? action,
        int? page, int? pageSize, CancellationToken ct = default)
    {
        var fromDate = from ?? DateTime.Today.AddDays(-30);
        var toDate = to ?? DateTime.Today.AddDays(1);
        var tableFiltre = string.IsNullOrWhiteSpace(table) ? null : table;
        var actionFiltre = string.IsNullOrWhiteSpace(action) ? null : action;
        return await PagedLists.PageAsync(
            _uow.Repository<suivi_SUIVI>(),
            e => e._DATETIMES >= fromDate
                 && e._DATETIMES <= toDate
                 && (tableFiltre == null || e._TABLES == tableFiltre)
                 && (actionFiltre == null || e._ACTIONS == actionFiltre),
            q => q.OrderByDescending(e => e._DATETIMES).ThenByDescending(e => e.Id),
            page,
            pageSize,
            e => new AuditEventDto(e.Id, e._TABLES, e._COLONNES, e._USERS, e._ACTIONS, e._DATETIMES, e._NOUVEAUX, e._ANCIENS, e._BASES),
            ct);
    }

    public async Task<IReadOnlyList<object>> ListDiscoveredSchemaAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<suivi_T>().ListAsync(_ => true, ct);
        return list.Select(t => (object)new { t.Id, t.S_TABLES, t.S_COLONNES, t.S_TYPES, t.BASES }).ToList();
    }
}
