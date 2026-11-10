namespace SoftTime.Application.DTOs;

/// <summary>Totaux d'atterrissage d'une semaine (lundi → dimanche), en heures et en nombre de salariés.</summary>
/// <param name="EnHeuresSup">Salariés qui finiront la semaine avec des heures supplémentaires.</param>
/// <param name="AuDela150">Dont ceux qui passent à 150 %.</param>
/// <param name="AuDelaPlafond">Dont ceux qui dépassent le plafond exonéré.</param>
/// <param name="SansPlanning">Salariés dont au moins un jour ouvré restant n'a pas de planning.</param>
/// <param name="HeuresARetirer">Heures à retirer des plannings restants pour repasser sous les seuils.</param>
public sealed record WeekLandingWeekDto(
    DateTime SemaineDebut,
    int Salaries,
    int EnHeuresSup,
    int AuDela150,
    int AuDelaPlafond,
    int SansPlanning,
    decimal HsAcquises,
    decimal HsAtterrissage,
    decimal HeuresARetirer);
