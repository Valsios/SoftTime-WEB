using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public class AuditService
{
    private readonly IUnitOfWork _uow;

    public AuditService(IUnitOfWork uow) => _uow = uow;

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
            await repo.AddAsync(new suivi_TBLCLN
            {
                TABLES = dto.Table,
                COLONNES = dto.Column,
                TYPES = dto.Type,
                ETATS = dto.Enabled,
                BASES = dto.Base
            }, ct);
        }
        else
        {
            var e = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            e.TABLES = dto.Table;
            e.COLONNES = dto.Column;
            e.TYPES = dto.Type;
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

    public async Task<IReadOnlyList<object>> ListDiscoveredSchemaAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<suivi_T>().ListAsync(_ => true, ct);
        return list.Select(t => (object)new { t.Id, t.S_TABLES, t.S_COLONNES, t.S_TYPES, t.BASES }).ToList();
    }
}
