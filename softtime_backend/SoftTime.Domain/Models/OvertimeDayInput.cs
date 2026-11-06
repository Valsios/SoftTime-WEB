#nullable enable

namespace SoftTime.Domain.Models;

/// <summary>
/// Journée soumise au calcul : paires entrée/sortie retenues (<c>T_HEURECORRIGER</c>) et contexte
/// (planning du jour, férié, congé, retard explicitement saisi).
/// </summary>
public sealed record OvertimeDayInput(
    DateTime Jour,
    IReadOnlyList<PunchPair> Paires,
    TimeSpan? PlanningHa = null,
    TimeSpan? PlanningHd = null,
    TimeSpan? PlanningPause = null,
    bool Off = false,
    bool Ferie = false,
    bool CorrectionPresente = false,
    decimal CongeValeur = 0m,
    TimeSpan? RetardExplicite = null);
