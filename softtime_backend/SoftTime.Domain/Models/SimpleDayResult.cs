namespace SoftTime.Domain.Models;

/// <summary>
/// Résultat de l'appariement d'une journée en mode simple : les paires retenues, les pointages
/// orphelins, la pause déduite du schéma « entrée / pause / reprise / sortie », et le motif
/// d'anomalie quand aucune paire n'est exploitable.
/// </summary>
public sealed class SimpleDayResult
{
    public static readonly SimpleDayResult Empty = new();

    public IReadOnlyList<PunchPair> Pairs { get; init; } = Array.Empty<PunchPair>();

    public IReadOnlyList<PunchMoment> Unpaired { get; init; } = Array.Empty<PunchMoment>();

    /// <summary>Début de pause lu sur le 2ᵉ pointage d'une journée à 4 pointages.</summary>
    public TimeSpan? Hps { get; init; }

    /// <summary>Fin de pause lue sur le 3ᵉ pointage d'une journée à 4 pointages.</summary>
    public TimeSpan? Hpe { get; init; }

    /// <summary>Motif d'anomalie (<see cref="Services.PunchAnomalyMotifs"/>), <c>null</c> si une paire existe.</summary>
    public string? Motif { get; init; }
}
