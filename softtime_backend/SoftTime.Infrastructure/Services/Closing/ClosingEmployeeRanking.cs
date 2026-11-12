using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Salariés les plus touchés d'une analyse : regroupement en mémoire des anomalies déjà calculées (aucune lecture),
/// rangé par poids cumulé (part dans la perte de score), puis nombre d'anomalies, puis matricule (ordre stable).
/// Plafonné à <see cref="MaxSalaries"/> lignes ; le nombre total de salariés concernés est rendu à part.
/// </summary>
internal static class ClosingEmployeeRanking
{
    /// <summary>Lignes renvoyées au plus : la liste des anomalies, filtrable par matricule, couvre le reste.</summary>
    internal const int MaxSalaries = 20;

    internal static (IReadOnlyList<ClosingEmployeeDto> Top, int Concernes) Classer(
        IReadOnlyList<ClosingAnomaly> anomalies,
        IReadOnlyDictionary<string, string?> departementParCle,
        ClosingRulesOptions options)
    {
        var groupes = anomalies
            .GroupBy(a => a.Matricule, StringComparer.Ordinal)
            .Select(g => (Cle: g.Key, Liste: g.ToList(), Poids: g.Sum(a => options.Poids(a.Severity))))
            .ToList();

        var top = groupes
            .OrderByDescending(g => g.Poids)
            .ThenByDescending(g => g.Liste.Count)
            .ThenBy(g => g.Cle, StringComparer.Ordinal)
            .Take(MaxSalaries)
            .Select(g => new ClosingEmployeeDto(
                g.Cle,
                departementParCle.GetValueOrDefault(g.Cle),
                g.Liste.Count,
                g.Poids,
                ClosingComputation.ComptagesGravite(g.Liste),
                TypesPresents(g.Liste)))
            .ToList();
        return (top, groupes.Count);
    }

    /// <summary>Comptage des seuls types présents, dans l'ordre de l'énumération.</summary>
    private static IReadOnlyList<ClosingCountDto> TypesPresents(IReadOnlyCollection<ClosingAnomaly> anomalies)
    {
        var comptes = ClosingComputation.CompterTypes(anomalies);
        return ClosingComputation.Types
            .Where(t => comptes[t] > 0)
            .Select(t => new ClosingCountDto(t.ToString(), comptes[t]))
            .ToList();
    }
}
