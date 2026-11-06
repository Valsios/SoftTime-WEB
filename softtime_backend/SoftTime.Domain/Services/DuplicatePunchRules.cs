using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class DuplicatePunchRules
{

    /// <summary>3.5 — Plusieurs pointages du même matricule à la même date et à la même heure exacte.</summary>
    public static IReadOnlyList<ClosingAnomaly> DoublonsExacts(
        IReadOnlyList<Pointage> pointages, ClosingScope scope)
    {
        return DoublonsExacts(ClosingPunchIndex.Build(pointages), scope);
    }

    /// <summary>3.5 sur un index de pointages déjà construit (pointages triés : les doublons sont contigus).</summary>
    public static IReadOnlyList<ClosingAnomaly> DoublonsExacts(
        ClosingPunchIndex pointages, ClosingScope scope)
    {
        var (from, to, multiPointage, options) = scope;
        options ??= ClosingRulesOptions.Default;
        var gravite = options.Gravite(ClosingAnomalyType.DoublonExact, multiPointage);
        var debut = from.Date;
        var fin = to.Date;

        var resultats = new List<ClosingAnomaly>();
        foreach (var cle in pointages.Cles)
        {
            var moments = pointages.Moments(cle);
            for (var i = 0; i < moments.Count;)
            {
                var j = i + 1;
                while (j < moments.Count && moments[j] == moments[i])
                    j++;

                var jour = moments[i].Date;
                if (j - i > 1 && jour >= debut && jour <= fin)
                    resultats.Add(ClosingAnomalyFactory.Anomalie(
                        ClosingAnomalyType.DoublonExact, cle, jour,
                        $"{j - i} pointages identiques à {ClosingAnomalyFactory.FormatHeure(moments[i].Heure)} ({ClosingAnomalyFactory.EffetDoublon(multiPointage)})",
                        gravite: gravite));
                i = j;
            }
        }

        return PayrollClosingRules.Trier(resultats);
    }

    /// <summary>
    /// 3.6 — Deux pointages successifs du même matricule séparés de strictement moins de
    /// <see cref="ClosingRulesOptions.DuplicateToleranceSeconds"/>. Les doublons exacts sont
    /// réduits à un seul instant avant la comparaison : ils ne sont jamais comptés deux fois.
    /// L'anomalie est datée du premier pointage de la paire.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> QuasiDoublons(
        IReadOnlyList<Pointage> pointages, ClosingScope scope)
    {
        return QuasiDoublons(ClosingPunchIndex.Build(pointages), scope);
    }

    /// <summary>3.6 sur un index de pointages déjà construit.</summary>
    public static IReadOnlyList<ClosingAnomaly> QuasiDoublons(
        ClosingPunchIndex pointages, ClosingScope scope)
    {
        var (from, to, multiPointage, options) = scope;
        options ??= ClosingRulesOptions.Default;
        var gravite = options.Gravite(ClosingAnomalyType.QuasiDoublon, multiPointage);
        var tolerance = TimeSpan.FromSeconds(Math.Max(0, options.DuplicateToleranceSeconds));
        var debut = from.Date;
        var fin = to.Date;

        var resultats = new List<ClosingAnomaly>();
        foreach (var cle in pointages.Cles)
        {
            // Pointages déjà triés : instants distincts de la période, dans l'ordre.
            var instants = new List<DateTime>();
            foreach (var m in pointages.Moments(cle))
            {
                if (m.Date < debut || m.Date > fin)
                    continue;
                var instant = m.Date + m.Heure;
                if (instants.Count == 0 || instants[^1] != instant)
                    instants.Add(instant);
            }

            for (var i = 1; i < instants.Count; i++)
            {
                var ecart = instants[i] - instants[i - 1];
                if (ecart >= tolerance)
                    continue;

                resultats.Add(ClosingAnomalyFactory.Anomalie(
                    ClosingAnomalyType.QuasiDoublon, cle, instants[i - 1].Date,
                    $"Écart de {(int)ecart.TotalSeconds} s entre {ClosingAnomalyFactory.FormatHeure(instants[i - 1].TimeOfDay)} et {ClosingAnomalyFactory.FormatHeure(instants[i].TimeOfDay)} ({ClosingAnomalyFactory.EffetDoublon(multiPointage)})",
                    gravite: gravite));
            }
        }

        return PayrollClosingRules.Trier(resultats);
    }
}
