namespace SoftTime.Application.DTOs;

// --- Tableau de bord analytique ---

public record DashboardStatsDto(
    int TotalEmployes,
    DashboardKpiDto Kpi,
    DashboardAbsencesDto Absences,
    DashboardRetardsDto Retards,
    IReadOnlyList<DashboardSerieDto> Series,
    DashboardRepartitionDto Repartition,
    DashboardAssiduiteDto Assiduite)
{
    public DashboardStatsDto() : this(default, default!, default!, default!,
        Array.Empty<DashboardSerieDto>(), default!, default!) { }
}
