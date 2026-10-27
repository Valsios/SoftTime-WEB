using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Regroupe les pointages d'un salarié par journée de travail, en tenant compte du planning.
///
/// Portage de la construction de <c>lPointages</c> du desktop (<c>Frm_Import.importa</c>) :
/// <list type="bullet">
///   <item>journée simple : pointages de la date ;</item>
///   <item>poste de nuit : pointages de la date d'entrée jusqu'à la fin effective, avec
///     rattachement des pointages de sortie au jour de nuit ;</item>
///   <item>sans planning : pointages de la date.</item>
/// </list>
///
/// Écart assumé avec le desktop : les pointages de sortie d'une nuit sont <b>consommés</b> et ne
/// sont donc pas retraités le lendemain. Le desktop sélectionnait toutes les lignes des dates
/// <c>HA.Date → HD.Date</c>, ce qui pouvait produire des doublons de corrections.
/// </summary>
public static class PunchDayGrouping
{
    /// <summary>
    /// Marge maximale après la fin planifiée pour rattacher une sortie de nuit au jour d'entrée.
    /// Elle couvre les heures supplémentaires raisonnables ; au-delà, le pointage appartient au
    /// jour suivant (et peut devenir une anomalie).
    /// </summary>
    public static readonly TimeSpan NightExitGrace = TimeSpan.FromHours(4);

    /// <summary>
    /// Construit, pour chaque jour de <paramref name="from"/> à <paramref name="to"/>, la liste
    /// triée des pointages rattachés à cette journée de travail.
    /// </summary>
    public static IReadOnlyDictionary<DateTime, IReadOnlyList<PunchMoment>> Build(
        IReadOnlyList<PunchMoment> punches,
        IReadOnlyDictionary<DateTime, PunchPlanningWindow> planningByDay,
        DateTime from,
        DateTime to,
        TimeSpan? nightExitGrace = null)
    {
        var result = new Dictionary<DateTime, IReadOnlyList<PunchMoment>>();
        from = from.Date;
        to = to.Date;
        if (to < from)
            return result;

        var grace = nightExitGrace ?? NightExitGrace;
        if (grace < TimeSpan.Zero)
            grace = TimeSpan.Zero;

        var ordered = punches
            .Where(p => p.Heure >= TimeSpan.Zero && p.Heure < TimeSpan.FromDays(1))
            .OrderBy(p => p.Date.Date)
            .ThenBy(p => p.Heure)
            .ToList();

        // Consommation par index : deux pointages à la même heure restent deux lignes distinctes.
        var consumed = new bool[ordered.Count];

        for (var jour = from; jour <= to; jour = jour.AddDays(1))
        {
            var moments = new List<PunchMoment>();

            for (var i = 0; i < ordered.Count; i++)
            {
                if (consumed[i] || ordered[i].Date.Date != jour)
                    continue;
                moments.Add(ordered[i]);
            }

            if (planningByDay.TryGetValue(jour, out var plan) && !plan.Off && plan.EstNuit)
            {
                var fin = plan.FinEffective!.Value;
                var borne = fin + grace;

                // Le planning du lendemain peut commencer avant la marge : on s'arrête à son début
                // pour ne pas aspirer la journée suivante dans la nuit.
                if (planningByDay.TryGetValue(jour.AddDays(1), out var suivant)
                    && suivant.Debut is DateTime debutSuivant
                    && debutSuivant > fin
                    && debutSuivant < borne)
                {
                    borne = debutSuivant;
                }

                for (var i = 0; i < ordered.Count; i++)
                {
                    if (consumed[i])
                        continue;

                    var moment = ordered[i];
                    if (moment.Date.Date <= jour)
                        continue;

                    var dateHeure = moment.Date.Date + moment.Heure;
                    if (dateHeure >= borne)
                        continue;

                    moments.Add(moment);
                    consumed[i] = true;
                }
            }

            moments.Sort(Compare);
            result[jour] = moments;
        }

        return result;
    }

    private static int Compare(PunchMoment gauche, PunchMoment droite)
    {
        var parDate = gauche.Date.Date.CompareTo(droite.Date.Date);
        return parDate != 0 ? parDate : gauche.Heure.CompareTo(droite.Heure);
    }
}
