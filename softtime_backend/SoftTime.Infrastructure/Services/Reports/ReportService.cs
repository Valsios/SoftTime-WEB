using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Reports;

public sealed class ReportService : IReportService
{
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IPointageReportService _pointageReportService;
    private readonly IAbsenceReportService _absenceReportService;
    private readonly IRetardReportService _retardReportService;
    private readonly IHoursReportService _hoursReportService;

    public ReportService(
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IPointageReportService pointageReportService,
        IAbsenceReportService absenceReportService,
        IRetardReportService retardReportService,
        IHoursReportService hoursReportService)
    {
        _tenant = tenant;
        _externalSource = externalSource;
        _pointageReportService = pointageReportService;
        _absenceReportService = absenceReportService;
        _retardReportService = retardReportService;
        _hoursReportService = hoursReportService;
    }

    /// <summary>Dispatcher du endpoint générique <c>GET /api/reports/{type}/paged</c>.</summary>
    public async Task<PagedResult<object>> PagedAsync(
        string type, ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        if (SearchText.Terms(filter.Search).Count > 0)
        {
            IReadOnlyList<object> rows = type switch
            {
                "pointage" => (await _pointageReportService.PointageAsync(filter, ct)).Cast<object>().ToList(),
                "absences" => (await _absenceReportService.AbsencesAsync(filter, ct)).Cast<object>().ToList(),
                "retards" => (await _retardReportService.CalculerRetardsAsync(filter, ct)).Cast<object>().ToList(),
                "hs-recap" => (await _hoursReportService.HsRecapAsync(filter, ct)).Cast<object>().ToList(),
                "heures-semaine" => (await _hoursReportService.HeuresParSemaineAsync(filter, ct)).ToList(),
                "heures-dimanche" => (await _hoursReportService.HeuresDimancheAsync(filter, ct)).ToList(),
                _ => throw new ArgumentException($"Type de rapport inconnu : {type}.")
            };
            var filtered = rows
                .Where(row => SearchText.MatchesObject(filter.Search, row))
                .ToList();
            return PagedLists.Page(filtered, page, pageSize);
        }

        return type switch
        {
            "pointage" => await _pointageReportService.PointagePagedAsync(filter, page, pageSize, ct),
            "absences" => await _absenceReportService.AbsencesPagedAsync(filter, page, pageSize, ct),
            "retards" => await _retardReportService.RetardsPagedAsync(filter, page, pageSize, ct),
            "hs-recap" => await _hoursReportService.HsRecapPagedAsync(filter, page, pageSize, ct),
            "heures-semaine" => await _hoursReportService.HeuresSemainePagedAsync(filter, page, pageSize, ct),
            "heures-dimanche" => await _hoursReportService.HeuresDimanchePagedAsync(filter, page, pageSize, ct),
            _ => throw new ArgumentException($"Type de rapport inconnu : {type}.")
        };
    }

    // ---------------------------------------------------------------- Congés

    public async Task<AbsenceInfo?> GetLeaveAsync(string matricule, DateTime date, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var excluded = new[] { "0200", "0300" };
        var allEvents = await _externalSource.GetEventsAsync(sageRow, ct);
        var codes = allEvents.Select(e => e.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !excluded.Contains(c))
            .Select(c => c!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var emp = (await _externalSource.GetEmployeesAsync(sageRow, ct))
            .FirstOrDefault(e => string.Equals(e.Matricule, matricule, StringComparison.OrdinalIgnoreCase));
        if (emp == null) return null;
        var events = (await _externalSource.GetEmployeeEventsAsync(sageRow, ct))
            .Where(e => e.EmployeeId == emp.EmployeeId && e.Code is not null && codes.Contains(e.Code))
            .ToList();
        foreach (var ghrs in events)
        {
            if (ghrs.Start is null || ghrs.End is null) continue;
            var days = (int)ghrs.End.Value.Subtract(ghrs.Start.Value).TotalDays;
            for (var i = 0; i <= days; i++)
            {
                var d = ghrs.Start.Value.Date.AddDays(i);
                if (d.DayOfWeek == DayOfWeek.Sunday) continue;
                if (d != date.Date) continue;
                var valeur = (ghrs.Matin == true || ghrs.ApresMidi == true) && days == 0 ? 0.5 : 1;
                return new AbsenceInfo { Date = d, Evenement = ghrs.Code ?? string.Empty, Matricule = matricule, Valeur = valeur };
            }
        }
        return null;
    }
}
