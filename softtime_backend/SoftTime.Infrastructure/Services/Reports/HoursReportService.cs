using System.Linq.Expressions;

using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Reports;

public sealed class HoursReportService : IHoursReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public HoursReportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<HsExoDto>> HsRecapAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(HsRecapPredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToHsExoDto).ToList();
    }

    /// <summary>Récap HS EXO/IMPO paginé.</summary>
    public async Task<PagedResult<object>> HsRecapPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.From(_uow.Repository<HsExoImp>(), HsRecapPredicate(filter, _tenant.SageDb), q => q.OrderBy(h => h.Matricule).ThenBy(h => h.Oid)).PageAsync(new PageRequest(page, pageSize), h => (object)ToHsExoDto(h), ct);
    }

    public async Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default)
    {
        ReportPeriods.EnsureMondayToSunday(filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        var hs = await _uow.Repository<HsValidee>().ListReadOnlyAsync(HeuresSemainePredicate(filter, _tenant.SageDb), ct);
        return hs.Select(ToHeuresSemaineRow).ToList();
    }

    /// <summary>Heures par semaine paginées (répartition EXO/IMPO calculée après la page).</summary>
    public async Task<PagedResult<object>> HeuresSemainePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        ReportPeriods.EnsureMondayToSunday(filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.From(_uow.Repository<HsValidee>(), HeuresSemainePredicate(filter, _tenant.SageDb), q => q.OrderBy(h => h.Matricules).ThenBy(h => h.Id)).PageAsync(new PageRequest(page, pageSize), ToHeuresSemaineRow, ct);
    }

    public async Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<HeureDimanche>().ListReadOnlyAsync(HeuresDimanchePredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToHeuresDimancheRow).ToList();
    }

    /// <summary>Heures dimanche paginées.</summary>
    public async Task<PagedResult<object>> HeuresDimanchePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.From(_uow.Repository<HeureDimanche>(), HeuresDimanchePredicate(filter, _tenant.SageDb), q => q.OrderBy(h => h.DateDim).ThenBy(h => h.MatriculeSage).ThenBy(h => h.Id)).PageAsync(new PageRequest(page, pageSize), ToHeuresDimancheRow, ct);
    }

    private static Expression<Func<HsExoImp, bool>> HsRecapPredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        return h => h.BddSage == sageDb && h.PeriodeDebut == from && h.PeriodeFin == to;
    }

    private static Expression<Func<HsValidee, bool>> HeuresSemainePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return h => h.BddSage == sageDb
            && h.DateDeb == from
            && h.DateFin == to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.Matricules, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.Matricules, matriculeTo) <= 0);
    }

    private static Expression<Func<HeureDimanche, bool>> HeuresDimanchePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return h => h.BddSage == sageDb && h.DateDim >= from && h.DateDim <= to;
    }

    private static HsExoDto ToHsExoDto(HsExoImp h) => new(
        h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
        h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence);

    private static object ToHeuresSemaineRow(HsValidee h)
    {
        var split = OvertimeRules.SplitWeekly(h.Hs ?? 0);
        return new
        {
            MATRICULES = h.Matricules,
            HT = h.Ht,
            HS = h.Hs,
            HS130 = split.Hs1,
            HS150 = split.Hs2,
            NUIT = h.Nuit,
            DIMANCHE = h.Dimanche,
            FERIES = h.Feries,
            RETARD = h.Retard
        };
    }

    private static object ToHeuresDimancheRow(HeureDimanche h) => new
    {
        MATRICULE_SAGE = h.MatriculeSage,
        Date_Dim = h.DateDim,
        h.HeureDebut,
        h.HeureFin,
        h.Valeur
    };
}
