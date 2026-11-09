namespace SoftTime.Application.DTOs;

/// <summary>Une semaine validée (lundi → dimanche) : heures et répartition 130 % / 150 % (règle
/// OvertimeRules.SplitWeekly).</summary>
public sealed record SelfWeekDto(
    DateTime Lundi,
    DateTime Dimanche,
    decimal HeuresTravaillees,
    decimal HeuresSupplementaires,
    decimal Hs130,
    decimal Hs150,
    decimal Nuit,
    decimal Dimanches,
    decimal Feries,
    decimal Retard,
    decimal Absence);
