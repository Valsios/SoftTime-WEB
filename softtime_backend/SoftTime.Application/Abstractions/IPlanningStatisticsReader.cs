namespace SoftTime.Application.Abstractions;

/// <summary>
/// Comptage des jours planifiés par salarié sur une période. Requête d'agrégat SQL
/// (<c>GROUP BY IDSAL</c>) pour éviter de charger toutes les lignes de <c>T_PLANNING_SAL</c>
/// lorsque seule la grille des salariés est affichée.
/// </summary>
public interface IPlanningStatisticsReader
{
    Task<IReadOnlyList<PlanningDayCount>> CountPlannedDaysAsync(
        string sageDb,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}

/// <summary>Nombre de jours planifiés pour une carte salarié.</summary>
public sealed record PlanningDayCount(int CardPaieId, int PlannedDays);
