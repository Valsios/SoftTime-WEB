namespace SoftTime.Application.DTOs;

/// <param name="Total">Absences explicites + jours planifiés sans pointage.</param>
/// <param name="PlanifiesSansPointage">
/// Jours planifiés (OffP = false) sans heure corrigée, hors week-end et férié.
/// Les congés SAGE ne sont pas encore exclus de ce compteur (voir LOT 9).
/// </param>
public record DashboardAbsencesDto(int Total, int Explicites, int PlanifiesSansPointage, IReadOnlyList<DashboardGroupDto> Stats, IReadOnlyList<DashboardTopDto> Top)
{
    public DashboardAbsencesDto() : this(default, default, default, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()) { }
}
