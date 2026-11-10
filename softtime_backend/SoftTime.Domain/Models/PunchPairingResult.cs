namespace SoftTime.Domain.Models;

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
