using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Reports;

namespace SoftTime.Tests;

/// <summary>Assemble les services de rapports (ex-ReportService) avec l'ancien constructeur et leur délègue chaque méthode.</summary>
internal sealed class ReportServices
{
    public ReportServices(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        ISageLeaveCalendar leaves,
        ILogger<ReportContextLoader>? logger = null)
    {
        var loader = new ReportContextLoader(uow, tenant, leaves, logger ?? NullLogger<ReportContextLoader>.Instance);
        Pointages = new PointageReportService(uow, tenant);
        Absences = new AbsenceReportService(tenant, loader);
        Retards = new RetardReportService(uow, tenant, loader);
        Heures = new HoursReportService(uow, tenant);
        Reports = new ReportService(tenant, externalSource, Pointages, Absences, Retards, Heures);
    }

    public PointageReportService Pointages { get; }

    public AbsenceReportService Absences { get; }

    public RetardReportService Retards { get; }

    public HoursReportService Heures { get; }

    public ReportService Reports { get; }

    public Task<PagedResult<object>> PagedAsync(string type, ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Reports.PagedAsync(type, filter, page, pageSize, ct);

    public Task<AbsenceInfo?> GetLeaveAsync(string matricule, DateTime date, CancellationToken ct = default)
        => Reports.GetLeaveAsync(matricule, date, ct);

    public Task<IReadOnlyList<CorrectedHourDto>> PointageAsync(ReportFilter filter, CancellationToken ct = default)
        => Pointages.PointageAsync(filter, ct);

    public Task<PagedResult<object>> PointagePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Pointages.PointagePagedAsync(filter, page, pageSize, ct);

    public Task<IReadOnlyList<AbsenceReportRow>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default)
        => Absences.AbsencesAsync(filter, ct);

    public Task<PagedResult<object>> AbsencesPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Absences.AbsencesPagedAsync(filter, page, pageSize, ct);

    public Task<IReadOnlyList<RetardReportRow>> RetardsAsync(ReportFilter filter, CancellationToken ct = default)
        => Retards.RetardsAsync(filter, ct);

    public Task<PagedResult<object>> RetardsPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Retards.RetardsPagedAsync(filter, page, pageSize, ct);

    public Task<IReadOnlyList<RetardReportRow>> CalculerRetardsAsync(ReportFilter filter, CancellationToken ct)
        => Retards.CalculerRetardsAsync(filter, ct);

    public Task<IReadOnlyList<HsExoDto>> HsRecapAsync(ReportFilter filter, CancellationToken ct = default)
        => Heures.HsRecapAsync(filter, ct);

    public Task<PagedResult<object>> HsRecapPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Heures.HsRecapPagedAsync(filter, page, pageSize, ct);

    public Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default)
        => Heures.HeuresParSemaineAsync(filter, ct);

    public Task<PagedResult<object>> HeuresSemainePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Heures.HeuresSemainePagedAsync(filter, page, pageSize, ct);

    public Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default)
        => Heures.HeuresDimancheAsync(filter, ct);

    public Task<PagedResult<object>> HeuresDimanchePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
        => Heures.HeuresDimanchePagedAsync(filter, page, pageSize, ct);
}
