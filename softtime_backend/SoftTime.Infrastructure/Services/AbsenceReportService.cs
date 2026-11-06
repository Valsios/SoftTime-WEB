using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class AbsenceReportService : IAbsenceReportService
{
    private readonly ITenantConnectionService _tenant;
    private readonly IReportContextLoader _reportContextLoader;

    public AbsenceReportService(
        ITenantConnectionService tenant,
        IReportContextLoader reportContextLoader)
    {
        _tenant = tenant;
        _reportContextLoader = reportContextLoader;
    }

    /// <summary>
    /// État des absences <b>non justifiées</b> : croisement planning / corrections / congés SAGE /
    /// fériés. Les règles (<see cref="AbsenceReportRules"/>) calculent les trois natures d'absence,
    /// mais l'état n'affiche que les journées planifiées sans aucun pointage. Les congés SAGE, les
    /// jours fériés, les week-ends, les jours OFF et les absences déclarées
    /// (<c>ABSENCE_AM</c> / <c>ABSENCE_PM</c>) servent uniquement à exclure le jour.
    /// </summary>
    public async Task<IReadOnlyList<AbsenceReportRow>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var ctx = await _reportContextLoader.ChargerContexteAsync(filter, ct);
        if (ctx is null)
            return Array.Empty<AbsenceReportRow>();

        return AbsenceReportRules.Calculer(
                new EmployeeSources(ctx.Cartes, ctx.Plannings, ctx.Corrections),
                new AbsenceCalendar(ctx.Conges, ctx.Feries),
                new ReportPeriod(filter.From.Date, filter.To.Date, DateTime.Today))
            .Where(r => r.Type == AbsenceReportRules.TypeNonJustifiee)
            .ToList();
    }

    /// <summary>
    /// État absences paginé. Délègue au calcul complet puis découpe en mémoire.
    /// TODO (phase adaptation) : remplacer par un vrai découpage SQL sur AbsenceReportRules.
    /// </summary>
    public async Task<PagedResult<object>> AbsencesPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = (await AbsencesAsync(filter, ct)).Cast<object>().ToList();
        return PagedLists.Page(rows, page, pageSize);
    }
}
