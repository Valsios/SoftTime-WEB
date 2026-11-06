namespace SoftTime.Application.Abstractions;

/// <summary>Nombre de jours planifiés pour une carte salarié.</summary>
public sealed record PlanningDayCount(int CardPaieId, int PlannedDays);
