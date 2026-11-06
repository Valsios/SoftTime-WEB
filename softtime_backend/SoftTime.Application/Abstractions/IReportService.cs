using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

/// <summary>Contrat de <c>ReportService</c>.</summary>
public interface IReportService
{
    Task<IReadOnlyList<CorrectedHourDto>> PointageAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>État pointage paginé : filtre/ordre/découpage délégués à SQL.</summary>
    Task<PagedResult<object>> PointagePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>
    /// État des absences <b>non justifiées</b> : croisement planning / corrections / congés SAGE /
    /// fériés. Les règles (<see cref="AbsenceReportRules"/>) calculent les trois natures d'absence,
    /// mais l'état n'affiche que les journées planifiées sans aucun pointage. Les congés SAGE, les
    /// jours fériés, les week-ends, les jours OFF et les absences déclarées
    /// (<c>ABSENCE_AM</c> / <c>ABSENCE_PM</c>) servent uniquement à exclure le jour.
    /// </summary>
    Task<IReadOnlyList<AbsenceReportRow>> AbsencesAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>
    /// État absences paginé. Délègue au calcul complet puis découpe en mémoire.
    /// TODO (phase adaptation) : remplacer par un vrai découpage SQL sur AbsenceReportRules.
    /// </summary>
    Task<PagedResult<object>> AbsencesPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>
    /// État retards : calcul complet (première entrée du jour vs planning + tolérance, comme le
    /// tableau de bord) puis écriture de <c>T_impr_RET</c> pour l'impression.
    /// </summary>
    Task<IReadOnlyList<RetardReportRow>> RetardsAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>État retards paginé, sans écriture de la table d'impression.</summary>
    Task<PagedResult<object>> RetardsPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<HsExoDto>> HsRecapAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>Récap HS EXO/IMPO paginé.</summary>
    Task<PagedResult<object>> HsRecapPagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<object>> HeuresParSemaineAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>Heures par semaine paginées (répartition EXO/IMPO calculée après la page).</summary>
    Task<PagedResult<object>> HeuresSemainePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<object>> HeuresDimancheAsync(ReportFilter filter, CancellationToken ct = default);

    /// <summary>Heures dimanche paginées.</summary>
    Task<PagedResult<object>> HeuresDimanchePagedAsync(ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    /// <summary>Dispatcher du endpoint générique <c>GET /api/reports/{type}/paged</c>.</summary>
    Task<PagedResult<object>> PagedAsync(string type, ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default);

    Task<AbsenceInfo?> GetLeaveAsync(string matricule, DateTime date, CancellationToken ct = default);
}
