namespace SoftTime.Application.DTOs;

/// <summary>Atterrissage d'une semaine : totaux, fraîcheur des pointages, seuils et détail par salarié.</summary>
/// <param name="DernierJourPointe">Dernier jour de pointages traités ; les jours suivants sont projetés.</param>
public sealed record WeekLandingDto(
    WeekLandingWeekDto Totaux,
    DateTime Aujourdhui,
    DateTime? DernierJourPointe,
    decimal SeuilMajoration150,
    decimal PlafondExonere,
    IReadOnlyList<WeekLandingRowDto> Lignes);
