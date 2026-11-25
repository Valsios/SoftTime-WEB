using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Reports;

public sealed class AbsenceReportService : IAbsenceReportService
{
    private const string TypeAbsences = "absences";
    private const string TypeAbsencesJustifiees = "absences-justifiees";

    private readonly ITenantConnectionService _tenant;
    private readonly IReportContextLoader _reportContextLoader;
    private readonly ILogger<AbsenceReportService>? _logger;

    public AbsenceReportService(
        ITenantConnectionService tenant,
        IReportContextLoader reportContextLoader,
        ILogger<AbsenceReportService>? logger = null)
    {
        _tenant = tenant;
        _reportContextLoader = reportContextLoader;
        _logger = logger;
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
        var chrono = new ReportChrono(_logger, TypeAbsences, filter);
        var rows = await AbsencesAsync(filter, chrono, ct);
        chrono.Journaliser("liste", rows.Count);
        return rows;
    }

    private async Task<IReadOnlyList<AbsenceReportRow>> AbsencesAsync(ReportFilter filter, ReportChrono chrono, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        // Ni tolérances ni catégories : elles ne servent qu'à l'état des retards.
        var ctx = await chrono.ChargerAsync(() => _reportContextLoader.ChargerContexteAsync(filter, avecTolerances: false, ct));
        if (ctx is null)
            return Array.Empty<AbsenceReportRow>();

        return chrono.Calculer(() => AbsenceReportRules.Calculer(
                new EmployeeSources(ctx.Cartes, ctx.Plannings, ctx.Corrections),
                new AbsenceCalendar(ctx.Conges, ctx.Feries),
                new ReportPeriod(filter.From.Date, filter.To.Date, DateTime.Today))
            .Where(r => r.Type == AbsenceReportRules.TypeNonJustifiee)
            .ToList());
    }

    /// <summary>
    /// État absences paginé. Délègue au calcul complet puis découpe en mémoire.
    /// TODO (phase adaptation) : remplacer par un vrai découpage SQL sur AbsenceReportRules.
    /// </summary>
    public async Task<PagedResult<object>> AbsencesPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        var chrono = new ReportChrono(_logger, TypeAbsences, filter);
        var rows = await AbsencesAsync(filter, chrono, ct);
        var resultat = chrono.Paginer(() => PagedLists.Page(rows.Cast<object>().ToList(), page, pageSize));
        chrono.Journaliser("pagine", rows.Count);
        return resultat;
    }

    /// <summary>
    /// État informatif des absences justifiées (voir <see cref="AbsenceReportRules.CalculerJustifiees"/>). Si les congés SAGE
    /// n'ont pas pu être lus, l'état ne peut pas être calculé de façon fiable : plutôt qu'une liste vide (qui ferait croire
    /// à l'absence de congés), une erreur explicite est renvoyée au client (400 avec le message ci-dessous).
    /// </summary>
    public async Task<IReadOnlyList<AbsenceReportRow>> AbsencesJustifieesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var chrono = new ReportChrono(_logger, TypeAbsencesJustifiees, filter);
        var rows = await AbsencesJustifieesAsync(filter, chrono, ct);
        chrono.Journaliser("liste", rows.Count);
        return rows;
    }

    private async Task<IReadOnlyList<AbsenceReportRow>> AbsencesJustifieesAsync(
        ReportFilter filter, ReportChrono chrono, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var ctx = await chrono.ChargerAsync(() => _reportContextLoader.ChargerContexteAsync(filter, avecTolerances: false, ct));
        if (ctx is null)
            return Array.Empty<AbsenceReportRow>();
        if (!ctx.CongesDisponibles)
            throw new DomainException(MessageCongesIndisponibles);

        var codesComptes = await _reportContextLoader.ChargerCodesAbsenceAsync(ct);
        return chrono.Calculer(() => AbsenceReportRules.CalculerJustifiees(
            new EmployeeSources(ctx.Cartes, ctx.Plannings, ctx.Corrections),
            new AbsenceCalendar(ctx.Conges, ctx.Feries),
            new ReportPeriod(filter.From.Date, filter.To.Date, DateTime.Today),
            codesComptes));
    }

    /// <summary>État des absences justifiées paginé : calcul complet puis découpage en mémoire.</summary>
    public async Task<PagedResult<object>> AbsencesJustifieesPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        var chrono = new ReportChrono(_logger, TypeAbsencesJustifiees, filter);
        var rows = await AbsencesJustifieesAsync(filter, chrono, ct);
        var resultat = chrono.Paginer(() => PagedLists.Page(rows.Cast<object>().ToList(), page, pageSize));
        chrono.Journaliser("pagine", rows.Count);
        return resultat;
    }

    internal const string MessageCongesIndisponibles =
        "Les congés SAGE sont indisponibles : l'état des absences justifiées ne peut pas être calculé. Réessayez plus tard ou contactez l'administrateur.";
}
