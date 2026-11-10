using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.Landing;

/// <summary>Un salarié, son paramétrage HS et ses journées de calcul (pointages et planning), triées par date.</summary>
public sealed record LandingEmployee(CardPaie Card, OvertimeWeekConfig Config, IReadOnlyList<OvertimeDayInput> Jours);
