#nullable enable

namespace SoftTime.Domain.Models;

/// <summary>
/// Résultat d'une semaine (lundi → dimanche). <see cref="Ht"/> est le total des heures créditées
/// (jours travaillés, fériés non travaillés, congés et complément du 8 mars, comme le desktop).
/// </summary>
public sealed record OvertimeWeekResult(
    decimal Ht,
    decimal Hs,
    decimal Nuit,
    decimal Dimanche,
    decimal Ferie,
    decimal Retard,
    decimal Absence,
    IReadOnlyList<OvertimeDayResult> Jours);
