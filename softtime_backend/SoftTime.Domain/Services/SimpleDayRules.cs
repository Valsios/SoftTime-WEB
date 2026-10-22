using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Appariement du mode simple (<c>MULTIPOINT ≠ 1</c>) avec un mode « fiable » optionnel.
///
/// <para>Comportement historique (<see cref="SimpleDayOptions.ParDefaut"/>) : 0 pointage → rien,
/// 1 pointage → orphelin, 2 pointages ou plus → une paire du premier au dernier.</para>
///
/// <para>Mode strict : les doublons de badge (≤ 60 s) sont absorbés, puis seuls 2 pointages
/// (entrée/sortie) ou 4 pointages (entrée / pause / reprise / sortie) sont exploitables. Toute
/// autre combinaison devient une anomalie motivée au lieu d'une paire silencieusement fausse. Les
/// bornes de durée, quand elles sont configurées, portent sur la durée nette de pause.</para>
/// </summary>
public static class SimpleDayRules
{
    public static SimpleDayResult Pair(IReadOnlyList<PunchMoment> points, SimpleDayOptions? options = null)
    {
        var o = options ?? SimpleDayOptions.ParDefaut;
        var ordered = points
            .OrderBy(p => p.Date.Date)
            .ThenBy(p => p.Heure)
            .ToList();

        if (ordered.Count == 0)
            return new SimpleDayResult { Motif = PunchAnomalyMotifs.AucunPointage };

        if (!o.Strict)
        {
            if (ordered.Count == 1)
                return new SimpleDayResult { Unpaired = ordered, Motif = PunchAnomalyMotifs.PointageUnique };

            var premier = ordered[0];
            var dernier = ordered[^1];
            return new SimpleDayResult
            {
                Pairs = new[] { new PunchPair(premier.Date, premier.Heure, dernier.Date, dernier.Heure) }
            };
        }

        var retenus = Dedoublonner(ordered, o.EffectiveDuplicateWindow);
        if (retenus.Count == 0)
            return new SimpleDayResult { Motif = PunchAnomalyMotifs.AucunPointage };
        if (retenus.Count == 1)
            return new SimpleDayResult { Unpaired = retenus, Motif = PunchAnomalyMotifs.PointageUnique };
        if (retenus.Count is not (2 or 4))
            return new SimpleDayResult { Unpaired = retenus, Motif = PunchAnomalyMotifs.PointagesIncoherents };

        var entree = retenus[0];
        var sortie = retenus[^1];
        TimeSpan? hps = null;
        TimeSpan? hpe = null;
        if (retenus.Count == 4)
        {
            hps = retenus[1].Heure;
            hpe = retenus[2].Heure;
        }

        var duree = sortie.Date.Date.Add(sortie.Heure) - entree.Date.Date.Add(entree.Heure);
        var pause = hps.HasValue && hpe.HasValue && hpe.Value > hps.Value ? hpe.Value - hps.Value : TimeSpan.Zero;
        var dureeNette = duree - pause;
        if (o.MinDuration is { } min && dureeNette < min)
            return new SimpleDayResult { Unpaired = retenus, Motif = PunchAnomalyMotifs.Duree };
        if (o.MaxDuration is { } max && dureeNette > max)
            return new SimpleDayResult { Unpaired = retenus, Motif = PunchAnomalyMotifs.Duree };

        return new SimpleDayResult
        {
            Pairs = new[] { new PunchPair(entree.Date, entree.Heure, sortie.Date, sortie.Heure) },
            Hps = hps,
            Hpe = hpe
        };
    }

    /// <summary>Supprime les pointages trop rapprochés du précédent retenu (doublon de badge).</summary>
    private static List<PunchMoment> Dedoublonner(IReadOnlyList<PunchMoment> ordered, TimeSpan fenetre)
    {
        var retenus = new List<PunchMoment>(ordered.Count);
        foreach (var point in ordered)
        {
            if (retenus.Count > 0)
            {
                var precedent = retenus[^1];
                var ecart = point.Date.Date.Add(point.Heure) - precedent.Date.Date.Add(precedent.Heure);
                if (ecart <= fenetre)
                    continue;
            }
            retenus.Add(point);
        }
        return retenus;
    }
}
