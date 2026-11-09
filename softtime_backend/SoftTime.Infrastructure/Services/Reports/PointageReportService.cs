using System.Linq.Expressions;

using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Reports;

public sealed class PointageReportService : IPointageReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public PointageReportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    // ---------------------------------------------------------------- États (lecture paginée)

    public async Task<IReadOnlyList<CorrectedHourDto>> PointageAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(PointagePredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToCorrectedHourDto).ToList();
    }

    /// <summary>État pointage paginé : filtre/ordre/découpage délégués à SQL.</summary>
    public async Task<PagedResult<object>> PointagePagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.From(_uow.Repository<HeureCorrigee>(), PointagePredicate(filter, _tenant.SageDb), q => q.OrderBy(h => h.DatePointageIn)
                  .ThenBy(h => h.MatriculeSage)
                  .ThenBy(h => h.Id)).PageAsync(new PageRequest(page, pageSize), h => (object)ToCorrectedHourDto(h), ct);
    }

    private static Expression<Func<HeureCorrigee, bool>> PointagePredicate(ReportFilter filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return h => h.BddSage == sageDb
            && h.DatePointageIn >= from
            && h.DatePointageIn <= to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(h.MatriculeSage, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(h.MatriculeSage, matriculeTo) <= 0);
    }

    private static CorrectedHourDto ToCorrectedHourDto(HeureCorrigee a) => new(
        a.Id, a.MatriculeSage, a.DatePointageIn, a.DatePointageOut, a.HEntree, a.Hps, a.Hpe, a.HSortie,
        a.AbsenceAm, a.AbsencePm, a.IntituleAbsence, a.Retard, a.Ferier, a.FerierAm, a.FerierPm,
        a.ValiderCorrection, a.ValiderHs, a.Hs, a.MNuit, a.MDimanche, a.MFeries);
}
