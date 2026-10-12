using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles d'appariement entrée/sortie portées depuis l'application desktop
/// (Services/Traitements/ImportationPointageService.cs) :
/// <list type="bullet">
///   <item>mode simple (<c>MULTIPOINT = false</c>) : 1er pointage = entrée, dernier = sortie ;</item>
///   <item>mode multi-pointage : appariement avec un seuil de 15 minutes entre deux pointages.</item>
/// </list>
/// Classe pure (aucune dépendance EF) afin d'être testable unitairement, sur le modèle de
/// <see cref="OvertimeRules"/>.
/// </summary>
public static class PunchPairingRules
{
    /// <summary>Écart minimal entre deux pointages pour ouvrir une nouvelle paire (mode multi-pointage).</summary>
    public static readonly TimeSpan MultiPunchGap = new(0, 15, 0);

    /// <summary>
    /// Mode simple : 0 pointage → rien, 1 pointage → orphelin (anomalie),
    /// 2 pointages ou plus → une paire du premier au dernier pointage.
    /// </summary>
    public static PunchPairingResult PairSimpleDay(IReadOnlyList<PunchMoment> points)
    {
        var ordered = Order(points);
        if (ordered.Count == 0)
            return PunchPairingResult.Empty;
        if (ordered.Count == 1)
            return new PunchPairingResult { Unpaired = ordered };

        var premier = ordered[0];
        var dernier = ordered[^1];
        return new PunchPairingResult
        {
            Pairs = new[] { new PunchPair(premier.Date, premier.Heure, dernier.Date, dernier.Heure) }
        };
    }

    /// <summary>
    /// Mode multi-pointage : porte la règle du desktop. Une paire est fermée au dernier pointage
    /// de la journée ; si le nombre de pointages retenus est impair, tous deviennent des orphelins
    /// (anomalies).
    /// </summary>
    public static PunchPairingResult PairMultipleDay(IReadOnlyList<PunchMoment> points, TimeSpan? gap = null)
    {
        var seuil = gap ?? MultiPunchGap;
        var ordered = Order(points);
        if (ordered.Count == 0)
            return PunchPairingResult.Empty;
        if (ordered.Count == 1)
            return new PunchPairingResult { Unpaired = ordered };

        // Pointages retenus après application du seuil (portage de « pointTraite »).
        var retenus = new List<PunchMoment> { ordered[0] };
        var dateEntree = ordered[0].Date.Date;
        var heureEntree = ordered[0].Heure;
        var finJournee = ordered[^1].Date.Date.Add(ordered[^1].Heure);

        for (var i = 1; i < ordered.Count; i++)
        {
            var point = ordered[i];
            var depuisEntree = point.Date.Date.Add(point.Heure) - dateEntree.Add(heureEntree);
            if (depuisEntree <= seuil)
                continue;
            var avantFin = finJournee - point.Date.Date.Add(point.Heure);
            if (avantFin > seuil)
            {
                retenus.Add(point);
                heureEntree = point.Heure;
                dateEntree = point.Date.Date;
            }
            else
            {
                // Dernier pointage de la journée : il ferme la paire courante.
                retenus.Add(new PunchMoment(point.Date.Date, finJournee.TimeOfDay));
                break;
            }
        }

        if (retenus.Count % 2 == 1)
            return new PunchPairingResult { Unpaired = retenus };

        var paires = new List<PunchPair>(retenus.Count / 2);
        for (var n = 0; n + 1 < retenus.Count; n += 2)
            paires.Add(new PunchPair(retenus[n].Date, retenus[n].Heure, retenus[n + 1].Date, retenus[n + 1].Heure));
        return new PunchPairingResult { Pairs = paires };
    }

    /// <summary>
    /// Les pointages sont toujours triés (date puis heure) : le desktop s'appuyait implicitement sur
    /// l'ordre de la base, ce qui rendait le résultat non déterministe.
    /// </summary>
    private static List<PunchMoment> Order(IReadOnlyList<PunchMoment> points) =>
        points.OrderBy(p => p.Date.Date).ThenBy(p => p.Heure).ToList();
}
