using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

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
public class HistoryArchiveService : IHistoryArchiveService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public HistoryArchiveService(IUnitOfWork uow, ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    // ---------------------------------------------------------------- Archivage (interne)

    public Task ArchivePunchesAsync(IReadOnlyCollection<Pointage> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(p => new HistoriquePointage
        {
            PointageId = p.IdPointage,
            NomBddSage = p.NomBddSage,
            NomBddPointeuse = p.NomBddPointeuse,
            MatriculeSage = p.MatriculeSage,
            DatePointage = p.DatePointage,
            HeurePointage = p.HeurePointage,
            DateImportation = p.DateImportation,
            IdUniquePointage = p.IdUniquePointage,
            TypePointage = p.TypePointage,
            DateSuppression = now
        }).ToList();
        return _uow.Repository<HistoriquePointage>().AddRangeAsync(archives, ct);
    }

    public Task ArchiveAnomaliesAsync(IReadOnlyCollection<HeureAnomalie> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(a => new HistoriqueAnomalie
        {
            AnomalieId = a.Id,
            BddSage = a.BddSage,
            BddPointeuse = a.BddPointeuse,
            MatriculeSage = a.MatriculeSage,
            DatePointageIn = a.DatePointageIn,
            DatePointageOut = a.DatePointageOut,
            H1 = a.H1,
            H2 = a.H2,
            HEntree = a.HEntree,
            Hps = a.Hps,
            Hpe = a.Hpe,
            HSortie = a.HSortie,
            AbsAm = a.AbsAm,
            AbsPm = a.AbsPm,
            Feries = a.Feries,
            FeriesAm = a.FeriesAm,
            FeriesPm = a.FeriesPm,
            IntituleAbsence = a.IntituleAbsence,
            DateSuppression = now
        }).ToList();
        return _uow.Repository<HistoriqueAnomalie>().AddRangeAsync(archives, ct);
    }

    public Task ArchiveHsAsync(IReadOnlyCollection<HsExoImp> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(h => new HistoriqueHs
        {
            HsId = h.Oid,
            Matricule = h.Matricule,
            NumSal = h.NumSal,
            Exo130 = h.Exo130,
            Exo150 = h.Exo150,
            I130 = h.I130,
            I150 = h.I150,
            PeriodeDebut = h.PeriodeDebut,
            PeriodeFin = h.PeriodeFin,
            Ferie = h.Ferie,
            Nuit = h.Nuit,
            Dim = h.Dim,
            Retard = h.Retard,
            Absence = h.Absence,
            BddSage = h.BddSage,
            BddPointeuse = h.BddPointeuse,
            DateSuppression = now
        }).ToList();
        return _uow.Repository<HistoriqueHs>().AddRangeAsync(archives, ct);
    }

    // ---------------------------------------------------------------- Consultation

    /// <summary>L'archive est filtrée sur sa propre date de suppression (quand a-t-on purgé ?).</summary>
    public async Task<IReadOnlyList<HistoryPointageDto>> ListPointagesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var rows = await _uow.Repository<HistoriquePointage>().ListReadOnlyAsync(
            h => h.NomBddSage == sageDb
                 && h.DateSuppression >= filter.From
                 && h.DateSuppression <= filter.To, ct);
        return rows
            .Where(h => DansPlage(h.MatriculeSage, filter))
            .OrderByDescending(h => h.DateSuppression)
            .Select(h => new HistoryPointageDto(h.Id, h.MatriculeSage, h.DatePointage, h.HeurePointage, h.TypePointage, h.DateSuppression))
            .ToList();
    }

    public async Task<IReadOnlyList<HistoryAnomalieDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var rows = await _uow.Repository<HistoriqueAnomalie>().ListReadOnlyAsync(
            h => h.BddSage == sageDb
                 && h.DateSuppression >= filter.From
                 && h.DateSuppression <= filter.To, ct);
        return rows
            .Where(h => DansPlage(h.MatriculeSage, filter))
            .OrderByDescending(h => h.DateSuppression)
            .Select(h => new HistoryAnomalieDto(h.Id, h.MatriculeSage, h.DatePointageIn, h.DatePointageOut,
                h.HEntree, h.HSortie, h.AbsAm, h.AbsPm, h.IntituleAbsence, h.DateSuppression))
            .ToList();
    }

    public async Task<IReadOnlyList<HistoryHsDto>> ListHsAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var rows = await _uow.Repository<HistoriqueHs>().ListReadOnlyAsync(
            h => h.BddSage == sageDb
                 && h.DateSuppression >= filter.From
                 && h.DateSuppression <= filter.To, ct);
        return rows
            .Where(h => DansPlage(h.Matricule, filter))
            .OrderByDescending(h => h.DateSuppression)
            .Select(h => new HistoryHsDto(h.Id, h.Matricule, h.PeriodeDebut, h.PeriodeFin,
                h.Exo130, h.Exo150, h.I130, h.I150, h.DateSuppression))
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
        System.Linq.Expressions.Expression<Func<HistoriquePointage, bool>> predicate =
            h => h.NomBddSage == sageDb
                 && h.DateSuppression >= from
                 && h.DateSuppression <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (h.MatriculeSage != null && string.Compare(h.MatriculeSage, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (h.MatriculeSage != null && string.Compare(h.MatriculeSage, matriculeTo) <= 0));
        var searchPredicate = SearchExpression.For<HistoriquePointage>(
            filter.Search, h => h.MatriculeSage, h => h.TypePointage);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        return await PagedLists.PageAsync(
            _uow.Repository<HistoriquePointage>(),
            predicate,
            q => q.OrderByDescending(h => h.DateSuppression).ThenByDescending(h => h.Id),
            page,
            pageSize,
            h => new HistoryPointageDto(h.Id, h.MatriculeSage, h.DatePointage, h.HeurePointage,
                h.TypePointage, h.DateSuppression),
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
        System.Linq.Expressions.Expression<Func<HistoriqueAnomalie, bool>> predicate =
            h => h.BddSage == sageDb
                 && h.DateSuppression >= from
                 && h.DateSuppression <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (h.MatriculeSage != null && string.Compare(h.MatriculeSage, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (h.MatriculeSage != null && string.Compare(h.MatriculeSage, matriculeTo) <= 0));
        var searchPredicate = SearchExpression.For<HistoriqueAnomalie>(
            filter.Search, h => h.MatriculeSage, h => h.IntituleAbsence);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        return await PagedLists.PageAsync(
            _uow.Repository<HistoriqueAnomalie>(),
            predicate,
            q => q.OrderByDescending(h => h.DateSuppression).ThenByDescending(h => h.Id),
            page,
            pageSize,
            h => new HistoryAnomalieDto(h.Id, h.MatriculeSage, h.DatePointageIn, h.DatePointageOut,
                h.HEntree, h.HSortie, h.AbsAm, h.AbsPm, h.IntituleAbsence, h.DateSuppression),
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
        System.Linq.Expressions.Expression<Func<HistoriqueHs, bool>> predicate =
            h => h.BddSage == sageDb
                 && h.DateSuppression >= from
                 && h.DateSuppression <= to
                 && (string.IsNullOrEmpty(matriculeFrom)
                     || (h.Matricule != null && string.Compare(h.Matricule, matriculeFrom) >= 0))
                 && (string.IsNullOrEmpty(matriculeTo)
                     || (h.Matricule != null && string.Compare(h.Matricule, matriculeTo) <= 0));
        var searchPredicate = SearchExpression.For<HistoriqueHs>(
            filter.Search, h => h.Matricule);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        return await PagedLists.PageAsync(
            _uow.Repository<HistoriqueHs>(),
            predicate,
            q => q.OrderByDescending(h => h.DateSuppression).ThenByDescending(h => h.Id),
            page,
            pageSize,
            h => new HistoryHsDto(h.Id, h.Matricule, h.PeriodeDebut, h.PeriodeFin,
                h.Exo130, h.Exo150, h.I130, h.I150, h.DateSuppression),
            ct);
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
