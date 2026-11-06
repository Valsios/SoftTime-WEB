using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services;

/// <summary>Lignes d'une purge de période ; les corrections manuelles conservées ne sont que comptées.</summary>
internal sealed record PunchPurge(
    List<Pointage> Pointages, List<HeureCorrigee> Corrections, List<HeureAnomalie> Anomalies, int ManuellesConservees)
{
    public bool EstVide => Pointages.Count == 0 && Corrections.Count == 0 && Anomalies.Count == 0;
}
