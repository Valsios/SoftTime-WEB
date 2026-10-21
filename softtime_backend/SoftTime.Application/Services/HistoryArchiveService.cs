using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Archives des suppressions : copie des lignes retirées de <c>T_POINTAGE</c>, <c>T_HEUREANOMALIE</c>
/// et <c>T_HSExoImp</c>. Portage des tables « Historique* » du desktop.
///
/// Ce n'est PAS un journal d'audit (qui, quelle colonne, quelle valeur) : c'est une corbeille
/// consultable et purgeable, alimentée au moment de la suppression. L'audit colonne par colonne
/// reste assuré par <see cref="AuditService"/> (<c>suivi_*</c>).
///
/// Les méthodes <c>Archive*</c> sont réservées à un usage interne : l'appelant a déjà contrôlé
/// l'autorisation et le périmètre des lignes. Les lectures et purges d'archive, elles, revérifient
/// le tenant.
/// </summary>
public class HistoryArchiveService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;

    public HistoryArchiveService(IUnitOfWork uow, TenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    // ---------------------------------------------------------------- Archivage (interne)

    public Task ArchivePunchesAsync(IReadOnlyCollection<T_POINTAGE> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(p => new T_HISTORIQUE_POINTAGE
        {
            POINTAGE_ID = p.IDPOINTAGE,
            NOM_BDD_SAGE = p.NOM_BDD_SAGE,
            NOM_BDD_POINTEUSE = p.NOM_BDD_POINTEUSE,
            MATRICULE_SAGE = p.MATRICULE_SAGE,
            DATE_POINTAGE = p.DATE_POINTAGE,
            HEURE_POINTAGE = p.HEURE_POINTAGE,
            DATE_IMPORTATION = p.DATE_IMPORTATION,
            IDUNIQUE_POINTAGE = p.IDUNIQUE_POINTAGE,
            TYPE_POINTAGE = p.TYPE_POINTAGE,
            DATE_SUPPRESSION = now
        }).ToList();
        return _uow.Repository<T_HISTORIQUE_POINTAGE>().AddRangeAsync(archives, ct);
    }

    public Task ArchiveAnomaliesAsync(IReadOnlyCollection<T_HEUREANOMALIE> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(a => new T_HISTORIQUE_ANOMALIE
        {
            ANOMALIE_ID = a.ID,
            BDD_SAGE = a.BDD_SAGE,
            BDD_POINTEUSE = a.BDD_POINTEUSE,
            MATRICULE_SAGE = a.MATRICULE_SAGE,
            DATE_POINTAGE_IN = a.DATE_POINTAGE_IN,
            DATE_POINTAGE_OUT = a.DATE_POINTAGE_OUT,
            H1 = a.H1,
            H2 = a.H2,
            HENTREE = a.HENTREE,
            HPS = a.HPS,
            HPE = a.HPE,
            HSORTIE = a.HSORTIE,
            ABS_AM = a.ABS_AM,
            ABS_PM = a.ABS_PM,
            FERIES = a.FERIES,
            FERIES_AM = a.FERIES_AM,
            FERIES_PM = a.FERIES_PM,
            INTITULE_ABSENCE = a.INTITULE_ABSENCE,
            DATE_SUPPRESSION = now
        }).ToList();
        return _uow.Repository<T_HISTORIQUE_ANOMALIE>().AddRangeAsync(archives, ct);
    }

    public Task ArchiveHsAsync(IReadOnlyCollection<T_HSExoImp> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(h => new T_HISTORIQUE_HS
        {
            HS_ID = h.OID,
            MATRICULE = h.Matricule,
            NUM_SAL = h.NumSal,
            EXO130 = h.EXO130,
            EXO150 = h.EXO150,
            I130 = h.I130,
            I150 = h.I150,
            PERIODE_DEBUT = h.PeriodeDebut,
            PERIODE_FIN = h.PeriodeFin,
            FERIE = h.FERIE,
            NUIT = h.NUIT,
            DIM = h.DIM,
            RETARD = h.RETARD,
            ABSENCE = h.ABSENCE,
            BDD_SAGE = h.BDD_SAGE,
            BDD_POINTEUSE = h.BDD_POINTEUSE,
            DATE_SUPPRESSION = now
        }).ToList();
        return _uow.Repository<T_HISTORIQUE_HS>().AddRangeAsync(archives, ct);
    }

    // ---------------------------------------------------------------- Consultation

    /// <summary>L'archive est filtrée sur sa propre date de suppression (quand a-t-on purgé ?).</summary>
    public async Task<IReadOnlyList<HistoryPointageDto>> ListPointagesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var rows = await _uow.Repository<T_HISTORIQUE_POINTAGE>().ListAsync(
            h => h.NOM_BDD_SAGE == sageDb
                 && h.DATE_SUPPRESSION >= filter.From
                 && h.DATE_SUPPRESSION <= filter.To, ct);
        return rows
            .Where(h => DansPlage(h.MATRICULE_SAGE, filter))
            .OrderByDescending(h => h.DATE_SUPPRESSION)
            .Select(h => new HistoryPointageDto(h.ID, h.MATRICULE_SAGE, h.DATE_POINTAGE, h.HEURE_POINTAGE, h.TYPE_POINTAGE, h.DATE_SUPPRESSION))
            .ToList();
    }

    public async Task<IReadOnlyList<HistoryAnomalieDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var rows = await _uow.Repository<T_HISTORIQUE_ANOMALIE>().ListAsync(
            h => h.BDD_SAGE == sageDb
                 && h.DATE_SUPPRESSION >= filter.From
                 && h.DATE_SUPPRESSION <= filter.To, ct);
        return rows
            .Where(h => DansPlage(h.MATRICULE_SAGE, filter))
            .OrderByDescending(h => h.DATE_SUPPRESSION)
            .Select(h => new HistoryAnomalieDto(h.ID, h.MATRICULE_SAGE, h.DATE_POINTAGE_IN, h.DATE_POINTAGE_OUT,
                h.HENTREE, h.HSORTIE, h.ABS_AM, h.ABS_PM, h.INTITULE_ABSENCE, h.DATE_SUPPRESSION))
            .ToList();
    }

    public async Task<IReadOnlyList<HistoryHsDto>> ListHsAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var rows = await _uow.Repository<T_HISTORIQUE_HS>().ListAsync(
            h => h.BDD_SAGE == sageDb
                 && h.DATE_SUPPRESSION >= filter.From
                 && h.DATE_SUPPRESSION <= filter.To, ct);
        return rows
            .Where(h => DansPlage(h.MATRICULE, filter))
            .OrderByDescending(h => h.DATE_SUPPRESSION)
            .Select(h => new HistoryHsDto(h.ID, h.MATRICULE, h.PERIODE_DEBUT, h.PERIODE_FIN,
                h.EXO130, h.EXO150, h.I130, h.I150, h.DATE_SUPPRESSION))
            .ToList();
    }

    // ---------------------------------------------------------------- Consultation paginée

    /// <summary>Archive des pointages paginée (filtre SQL date + matricule, tri date de suppression).</summary>
    public async Task<PagedResult<HistoryPointageDto>> ListPointagesPagedAsync(
        PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return await PagedLists.PageAsync(
            _uow.Repository<T_HISTORIQUE_POINTAGE>(),
            h => h.NOM_BDD_SAGE == sageDb
                 && h.DATE_SUPPRESSION >= from
                 && h.DATE_SUPPRESSION <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (h.MATRICULE_SAGE != null && string.Compare(h.MATRICULE_SAGE, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (h.MATRICULE_SAGE != null && string.Compare(h.MATRICULE_SAGE, matriculeTo) <= 0)),
            q => q.OrderByDescending(h => h.DATE_SUPPRESSION).ThenByDescending(h => h.ID),
            page,
            pageSize,
            h => new HistoryPointageDto(h.ID, h.MATRICULE_SAGE, h.DATE_POINTAGE, h.HEURE_POINTAGE,
                h.TYPE_POINTAGE, h.DATE_SUPPRESSION),
            ct);
    }

    /// <summary>Archive des anomalies paginée (filtre SQL date + matricule).</summary>
    public async Task<PagedResult<HistoryAnomalieDto>> ListAnomaliesPagedAsync(
        PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return await PagedLists.PageAsync(
            _uow.Repository<T_HISTORIQUE_ANOMALIE>(),
            h => h.BDD_SAGE == sageDb
                 && h.DATE_SUPPRESSION >= from
                 && h.DATE_SUPPRESSION <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (h.MATRICULE_SAGE != null && string.Compare(h.MATRICULE_SAGE, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (h.MATRICULE_SAGE != null && string.Compare(h.MATRICULE_SAGE, matriculeTo) <= 0)),
            q => q.OrderByDescending(h => h.DATE_SUPPRESSION).ThenByDescending(h => h.ID),
            page,
            pageSize,
            h => new HistoryAnomalieDto(h.ID, h.MATRICULE_SAGE, h.DATE_POINTAGE_IN, h.DATE_POINTAGE_OUT,
                h.HENTREE, h.HSORTIE, h.ABS_AM, h.ABS_PM, h.INTITULE_ABSENCE, h.DATE_SUPPRESSION),
            ct);
    }

    /// <summary>Archive des calculs HS paginée (filtre SQL date + matricule).</summary>
    public async Task<PagedResult<HistoryHsDto>> ListHsPagedAsync(
        PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return await PagedLists.PageAsync(
            _uow.Repository<T_HISTORIQUE_HS>(),
            h => h.BDD_SAGE == sageDb
                 && h.DATE_SUPPRESSION >= from
                 && h.DATE_SUPPRESSION <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (h.MATRICULE != null && string.Compare(h.MATRICULE, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (h.MATRICULE != null && string.Compare(h.MATRICULE, matriculeTo) <= 0)),
            q => q.OrderByDescending(h => h.DATE_SUPPRESSION).ThenByDescending(h => h.ID),
            page,
            pageSize,
            h => new HistoryHsDto(h.ID, h.MATRICULE, h.PERIODE_DEBUT, h.PERIODE_FIN,
                h.EXO130, h.EXO150, h.I130, h.I150, h.DATE_SUPPRESSION),
            ct);
    }

    // ---------------------------------------------------------------- Purge de l'archive

    public async Task<int> DeletePointagesAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<T_HISTORIQUE_POINTAGE>();
        var entity = (await repo.ListAsync(h => h.NOM_BDD_SAGE == sageDb, ct)).FirstOrDefault(h => h.ID == id);
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
        var repo = _uow.Repository<T_HISTORIQUE_ANOMALIE>();
        var entity = (await repo.ListAsync(h => h.BDD_SAGE == sageDb, ct)).FirstOrDefault(h => h.ID == id);
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
        var repo = _uow.Repository<T_HISTORIQUE_HS>();
        var entity = (await repo.ListAsync(h => h.BDD_SAGE == sageDb, ct)).FirstOrDefault(h => h.ID == id);
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
        var repo = _uow.Repository<T_HISTORIQUE_POINTAGE>();
        var entities = await repo.ListAsync(h => h.NOM_BDD_SAGE == sageDb, ct);
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
        var repo = _uow.Repository<T_HISTORIQUE_ANOMALIE>();
        var entities = await repo.ListAsync(h => h.BDD_SAGE == sageDb, ct);
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
        var repo = _uow.Repository<T_HISTORIQUE_HS>();
        var entities = await repo.ListAsync(h => h.BDD_SAGE == sageDb, ct);
        if (entities.Count > 0)
        {
            repo.RemoveRange(entities);
            await _uow.SaveChangesAsync(ct);
        }
        return new HistoryDeleteResultDto(entities.Count,
            entities.Count == 0 ? "Aucune archive de calcul HS à purger." : $"{entities.Count} archive(s) de calcul HS purgée(s).");
    }

    // ---------------------------------------------------------------- Interne

    private static bool DansPlage(string? matricule, PeriodRequest filter)
    {
        if (string.IsNullOrEmpty(filter.MatriculeFrom) && string.IsNullOrEmpty(filter.MatriculeTo))
            return true;
        if (string.IsNullOrEmpty(matricule))
            return false;
        if (!string.IsNullOrEmpty(filter.MatriculeFrom)
            && string.Compare(matricule, filter.MatriculeFrom, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (!string.IsNullOrEmpty(filter.MatriculeTo)
            && string.Compare(matricule, filter.MatriculeTo, StringComparison.OrdinalIgnoreCase) > 0)
            return false;
        return true;
    }
}
