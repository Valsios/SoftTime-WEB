using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class WorkIntervals
{

    // ------------------------------------------------------------------ communs

    internal static decimal Arrondir(decimal heures) => Math.Round(heures, 2);

    internal static TimeSpan Somme(IEnumerable<Intervalle> intervalles)
    {
        var total = TimeSpan.Zero;
        foreach (var intervalle in intervalles)
            total += intervalle.Duree;
        return total;
    }

    internal static decimal Heures(TimeSpan duree) => Arrondir((decimal)duree.TotalHours);

    internal static TimeSpan PauseTheorique(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = jour.PlanningPause ?? config.PauseCategorie ?? OvertimeRules.DefaultPause;
        return pause < TimeSpan.Zero ? TimeSpan.Zero : pause;
    }

    internal static List<PunchPair> Trier(IReadOnlyList<PunchPair>? paires)
        => (paires ?? Array.Empty<PunchPair>())
            .OrderBy(p => p.EntreeDate.Date)
            .ThenBy(p => p.EntreeHeure)
            .ToList();

    /// <summary>
    /// Durée prévue d'une journée en mode 2 pointages : <c>(HD − HA) − pause</c>. Quand le planning
    /// manque, il est reconstruit avec les heures réelles, comme le desktop (<c>planning = new T_PLANNING_SAL()</c>),
    /// d'où l'utilisation des dates des paires pour un poste de nuit.
    /// </summary>
    internal static decimal PrevuJournalier(
        OvertimeDayInput jour, PunchPair premiere, PunchPair derniere, TimeSpan pause)
    {
        var ha = jour.PlanningHa ?? premiere.EntreeHeure;
        var hd = jour.PlanningHd ?? derniere.SortieHeure;
        var debut = premiere.EntreeDate.Date + ha;
        var fin = derniere.SortieDate.Date + hd;
        var prevu = (decimal)(fin - debut).TotalHours - (decimal)pause.TotalHours;
        return prevu > 0m ? Arrondir(prevu) : 0m;
    }

    /// <summary>
    /// Absence non justifiée : jour planifié non chômé, sans aucune heure corrigée, hors congé et hors férié.
    /// Portage de l'absence du desktop (§7) avec deux corrections assumées : congés et fériés exclus.
    /// </summary>
    internal static decimal Absence(OvertimeDayInput jour, TimeSpan pause)
    {
        if (jour.Off || jour.CorrectionPresente || jour.CongeValeur > 0m || jour.Ferie)
            return 0m;
        if (!jour.PlanningHa.HasValue || !jour.PlanningHd.HasValue)
            return 0m;

        var prevu = (decimal)(jour.PlanningHd.Value - jour.PlanningHa.Value).TotalHours
                    - (decimal)pause.TotalHours;
        return prevu > 0m ? Arrondir(prevu) : 0m;
    }

    /// <summary>Retard = (première entrée réelle + tolérance) − heure planifiée, jamais négatif (synthèse §9-5).</summary>
    internal static decimal CalculerRetard(OvertimeWeekConfig config, OvertimeDayInput jour, IReadOnlyList<PunchPair> paires)
    {
        if (jour.RetardExplicite.HasValue)
            return Math.Max(0m, Arrondir((decimal)jour.RetardExplicite.Value.TotalHours));
        if (!jour.PlanningHa.HasValue || paires.Count == 0)
            return 0m;

        var autorise = jour.PlanningHa.Value + TimeSpan.FromMinutes(config.ToleranceEntreeMinutes);
        var retard = paires[0].EntreeHeure - autorise;
        return retard > TimeSpan.Zero ? Arrondir((decimal)retard.TotalHours) : 0m;
    }

    /// <summary>Arrondi de la sortie vers le bas au pas de tolérance (synthèse §10-12, secondes conservées).</summary>
    internal static TimeSpan ArrondirSortie(TimeSpan sortie, int? pasMinutes)
    {
        if (pasMinutes is not int pas || pas <= 0)
            return sortie;
        var minutes = OvertimeRules.GetMinuteSortie(sortie.Minutes, pas);
        return new TimeSpan(sortie.Hours, minutes, sortie.Seconds);
    }

    /// <summary>
    /// Intervalles réellement travaillés : entrée planifiée plancher (arrivée en avance non payée),
    /// sortie arrondie, découpe à minuit pour dater les deux fractions d'un poste de nuit.
    /// </summary>
    internal static List<Intervalle> Intervalles(
        IReadOnlyList<PunchPair> paires, TimeSpan? planningHa, int? pasSortie)
    {
        var resultats = new List<Intervalle>();
        foreach (var paire in paires)
        {
            var debut = paire.EntreeDate.Date + paire.EntreeHeure;
            if (planningHa.HasValue && paire.EntreeDate.Date == paire.SortieDate.Date
                && paire.EntreeHeure < planningHa.Value)
            {
                debut = paire.EntreeDate.Date + planningHa.Value;
            }

            var fin = paire.SortieDate.Date + ArrondirSortie(paire.SortieHeure, pasSortie);
            if (fin <= debut)
                continue;

            var curseur = debut;
            while (true)
            {
                if (fin <= curseur)
                    break;
                var minuit = curseur.Date.AddDays(1);
                if (fin < minuit)
                {
                    resultats.Add(new Intervalle(curseur, fin));
                    break;
                }
                resultats.Add(new Intervalle(curseur, minuit));
                curseur = minuit;
                if (curseur == fin)
                    break;
            }
        }
        return resultats;
    }

    /// <summary>Part d'un intervalle tombant dans la plage de nuit 22:00 → 05:00 (synthèse §4.3 d).</summary>
    internal static TimeSpan NuitEntre(DateTime debut, DateTime fin)
        => Intersection(debut, fin, debut.Date, TimeSpan.Zero, OvertimeRules.NightEnd)
           + Intersection(debut, fin, debut.Date, OvertimeRules.NightStart, TimeSpan.FromDays(1));

    internal static TimeSpan Intersection(DateTime debut, DateTime fin, DateTime jour, TimeSpan de, TimeSpan a)
    {
        var borneDebut = jour + de;
        var borneFin = jour + a;
        var d = debut > borneDebut ? debut : borneDebut;
        var f = fin < borneFin ? fin : borneFin;
        return f > d ? f - d : TimeSpan.Zero;
    }

    internal readonly record struct Intervalle(DateTime Debut, DateTime Fin)
    {
        public TimeSpan Duree => Fin - Debut;
    }
}
