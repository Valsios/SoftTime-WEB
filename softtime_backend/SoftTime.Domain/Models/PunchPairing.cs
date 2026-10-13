namespace SoftTime.Domain.Models;

/// <summary>Pointage élémentaire (date + heure) utilisé par les règles d'appariement entrée/sortie.</summary>
public sealed record PunchMoment(DateTime Date, TimeSpan Heure);

/// <summary>Paire entrée/sortie retenue pour une journée.</summary>
public sealed record PunchPair(DateTime EntreeDate, TimeSpan EntreeHeure, DateTime SortieDate, TimeSpan SortieHeure);

/// <summary>
/// Résultat d'un appariement de pointages : les paires cohérentes d'un côté, les pointages
/// orphelins (qui deviennent des anomalies) de l'autre.
/// </summary>
public sealed class PunchPairingResult
{
    public static readonly PunchPairingResult Empty = new();

    public IReadOnlyList<PunchPair> Pairs { get; init; } = Array.Empty<PunchPair>();

    public IReadOnlyList<PunchMoment> Unpaired { get; init; } = Array.Empty<PunchMoment>();
}
