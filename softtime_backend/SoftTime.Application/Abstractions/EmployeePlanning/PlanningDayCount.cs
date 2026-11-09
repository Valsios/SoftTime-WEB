namespace SoftTime.Application.Abstractions.EmployeePlanning;

/// <summary>Nombre de jours planifiés pour une carte salarié.</summary>
public sealed record PlanningDayCount(int CardPaieId, int PlannedDays);
