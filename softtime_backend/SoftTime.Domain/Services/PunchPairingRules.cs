using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles d'appariement entrée/sortie portées depuis l'application desktop
/// (<c>Frm_Import.importa</c>) :
/// <list type="bullet">
///   <item>mode simple (<c>MULTIPOINT = false</c>) : 1er pointage = entrée, dernier = sortie ;</item>
///   <item>mode multi-pointage : appariement avec un seuil de 15 minutes entre deux pointages,
///     dernier pointage forcé en sortie, puis règle de parité pair → corrections, impair → anomalies.</item>
/// </list>
/// Classe pure (aucune dépendance EF) afin d'être testable unitairement, sur le modèle de
/// <see cref="OvertimeRules"/>. Le regroupement des pointages par journée de travail (et la gestion
/// des postes de nuit) est assuré par <see cref="PunchDayGrouping"/>.
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
        var ordered = Order(points);
        if (ordered.Count == 0)
            return PunchPairingResult.Empty;
        if (ordered.Count == 1)
            return new PunchPairingResult { Unpaired = ordered };

        var retenus = Retenir(ordered, gap ?? MultiPunchGap);
        if (retenus.Count % 2 == 1)
            return new PunchPairingResult { Unpaired = retenus };

        var paires = new List<PunchPair>(retenus.Count / 2);
        for (var n = 0; n + 1 < retenus.Count; n += 2)
            paires.Add(new PunchPair(retenus[n].Date, retenus[n].Heure, retenus[n + 1].Date, retenus[n + 1].Heure));
        return new PunchPairingResult { Pairs = paires };
    }

    /// <summary>
    /// Pointages retenus après application du seuil (portage de « pointTraite ») : un pointage trop
    /// proche du précédent retenu est ignoré ; le premier pointage proche de la fin de journée ferme
    /// la paire courante à l'heure du dernier pointage.
    /// </summary>
    private static List<PunchMoment> Retenir(List<PunchMoment> ordered, TimeSpan seuil)
    {
        var retenus = new List<PunchMoment> { ordered[0] };
        var entree = ordered[0].Date.Date.Add(ordered[0].Heure);
        var finJournee = ordered[^1].Date.Date.Add(ordered[^1].Heure);
        foreach (var point in ordered.Skip(1))
        {
            var instant = point.Date.Date.Add(point.Heure);
            if (instant - entree <= seuil)
                continue;
            if (finJournee - instant <= seuil)
            {
                retenus.Add(new PunchMoment(point.Date.Date, finJournee.TimeOfDay));
                break;
            }
            retenus.Add(point);
            entree = instant;
        }
        return retenus;
    }

    /// <summary>
    /// Les pointages sont toujours triés (date puis heure) : le desktop s'appuyait implicitement sur
    /// l'ordre de la base, ce qui rendait le résultat non déterministe.
    /// </summary>
    private static List<PunchMoment> Order(IReadOnlyList<PunchMoment> points) =>
        points.OrderBy(p => p.Date.Date).ThenBy(p => p.Heure).ToList();
}
