using SoftTime.Application.Abstractions.History;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.History;

public sealed class HistoryQueryService : IHistoryQueryService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public HistoryQueryService(
        IUnitOfWork uow,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
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
            .Where(h => HistoryPeriods.DansPlage(h.MatriculeSage, filter))
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
            .Where(h => HistoryPeriods.DansPlage(h.MatriculeSage, filter))
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
            .Where(h => HistoryPeriods.DansPlage(h.Matricule, filter))
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
        return await PagedLists.From(_uow.Repository<HistoriquePointage>(), predicate, q => q.OrderByDescending(h => h.DateSuppression).ThenByDescending(h => h.Id)).PageAsync(new PageRequest(page, pageSize), h => new HistoryPointageDto(h.Id, h.MatriculeSage, h.DatePointage, h.HeurePointage,
                h.TypePointage, h.DateSuppression), ct);
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
        return await PagedLists.From(_uow.Repository<HistoriqueAnomalie>(), predicate, q => q.OrderByDescending(h => h.DateSuppression).ThenByDescending(h => h.Id)).PageAsync(new PageRequest(page, pageSize), h => new HistoryAnomalieDto(h.Id, h.MatriculeSage, h.DatePointageIn, h.DatePointageOut,
                h.HEntree, h.HSortie, h.AbsAm, h.AbsPm, h.IntituleAbsence, h.DateSuppression), ct);
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
        return await PagedLists.From(_uow.Repository<HistoriqueHs>(), predicate, q => q.OrderByDescending(h => h.DateSuppression).ThenByDescending(h => h.Id)).PageAsync(new PageRequest(page, pageSize), h => new HistoryHsDto(h.Id, h.Matricule, h.PeriodeDebut, h.PeriodeFin,
                h.Exo130, h.Exo150, h.I130, h.I150, h.DateSuppression), ct);
    }
}
