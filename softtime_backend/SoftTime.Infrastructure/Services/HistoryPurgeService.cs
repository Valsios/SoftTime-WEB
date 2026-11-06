using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class HistoryPurgeService : IHistoryPurgeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public HistoryPurgeService(
        IUnitOfWork uow,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    // ---------------------------------------------------------------- Purge de l'archive

    public async Task<int> DeletePointagesAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HistoriquePointage>();
        var entity = (await repo.ListAsync(h => h.NomBddSage == sageDb, ct)).FirstOrDefault(h => h.Id == id);
        if (entity is null)
            return 0;
        repo.Remove(entity);
        await _uow.SaveChangesAsync(ct);
        return 1;
    }

    public async Task<int> DeleteAnomaliesAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HistoriqueAnomalie>();
        var entity = (await repo.ListAsync(h => h.BddSage == sageDb, ct)).FirstOrDefault(h => h.Id == id);
        if (entity is null)
            return 0;
        repo.Remove(entity);
        await _uow.SaveChangesAsync(ct);
        return 1;
    }

    public async Task<int> DeleteHsAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HistoriqueHs>();
        var entity = (await repo.ListAsync(h => h.BddSage == sageDb, ct)).FirstOrDefault(h => h.Id == id);
        if (entity is null)
            return 0;
        repo.Remove(entity);
        await _uow.SaveChangesAsync(ct);
        return 1;
    }

    public async Task<HistoryDeleteResultDto> DeleteAllPointagesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HistoriquePointage>();
        var entities = await repo.ListAsync(h => h.NomBddSage == sageDb, ct);
        if (entities.Count > 0)
        {
            repo.RemoveRange(entities);
            await _uow.SaveChangesAsync(ct);
        }
        return new HistoryDeleteResultDto(entities.Count,
            entities.Count == 0 ? "Aucune archive de pointage à purger." : $"{entities.Count} archive(s) de pointage purgée(s).");
    }

    public async Task<HistoryDeleteResultDto> DeleteAllAnomaliesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HistoriqueAnomalie>();
        var entities = await repo.ListAsync(h => h.BddSage == sageDb, ct);
        if (entities.Count > 0)
        {
            repo.RemoveRange(entities);
            await _uow.SaveChangesAsync(ct);
        }
        return new HistoryDeleteResultDto(entities.Count,
            entities.Count == 0 ? "Aucune archive d'anomalie à purger." : $"{entities.Count} archive(s) d'anomalie purgée(s).");
    }

    public async Task<HistoryDeleteResultDto> DeleteAllHsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<HistoriqueHs>();
        var entities = await repo.ListAsync(h => h.BddSage == sageDb, ct);
        if (entities.Count > 0)
        {
            repo.RemoveRange(entities);
            await _uow.SaveChangesAsync(ct);
        }
        return new HistoryDeleteResultDto(entities.Count,
            entities.Count == 0 ? "Aucune archive de calcul HS à purger." : $"{entities.Count} archive(s) de calcul HS purgée(s).");
    }
}
