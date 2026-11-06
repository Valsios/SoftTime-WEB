using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

public interface IAbsenceReportService
{
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
}
