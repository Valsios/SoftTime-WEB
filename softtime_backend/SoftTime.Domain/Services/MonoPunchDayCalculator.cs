using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class MonoPunchDayCalculator
{

    // ------------------------------------------------------------------ 2 pointages

    internal static OvertimeDayResult JourMono(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = WorkIntervals.PauseTheorique(config, jour);
        var paires = WorkIntervals.Trier(jour.Paires);
        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var ferie = jour.Ferie;
        var samedi = jour.Jour.DayOfWeek == DayOfWeek.Saturday;
        var conge = jour.CongeValeur > 0m && !dimanche && !samedi;

        if (paires.Count == 0)
        {
            var retenu = conge
                ? WorkIntervals.Arrondir(config.Journee * jour.CongeValeur)
                : MultiPunchDayCalculator.JourneeCrediteeSansTravail(config, jour, dimanche, ferie);
            return new OvertimeDayResult(
                jour.Jour.Date, null, null, 0m, retenu,
                0m, 0m, 0m, 0m,
                WorkIntervals.Absence(jour, pause),
                0m, Conge: conge, JourTravaille: false, NombrePaires: 0);
        }

        var prevu = WorkIntervals.PrevuJournalier(jour, paires[0], paires[^1], pause);
        var entreePlanifiee = jour.PlanningHa ?? paires[0].EntreeHeure;

        decimal reel = 0m, retenuTotal = 0m, nuit = 0m, dim = 0m, ferieH = 0m, hs = 0m;
        foreach (var paire in paires)
        {
            var debut = paire.EntreeDate.Date + paire.EntreeHeure;
            if (paire.EntreeDate.Date == paire.SortieDate.Date && paire.EntreeHeure < entreePlanifiee)
            {
                debut = paire.EntreeDate.Date + entreePlanifiee;
            }

            var sortieHeure = WorkIntervals.ArrondirSortie(paire.SortieHeure, OvertimeRules.MonoExitRoundingMinutes);
            var fin = paire.SortieDate.Date + sortieHeure;
            if (fin <= debut)
                continue;

            var brut = WorkIntervals.Heures(fin - debut);
            var retenu = Math.Max(0m, brut - WorkIntervals.Heures(pause));
            reel += brut;
            retenuTotal += retenu;

            if (!dimanche && !ferie)
            {
                // Nuit : simple tranche 22:00 → 05:00 (synthèse §4.2).
                nuit += WorkIntervals.Heures(WorkIntervals.NuitEntre(debut, fin));
                // Samedi : toutes les heures sont supplémentaires (synthèse §4.2).
                hs += samedi ? retenu : Math.Max(0m, retenu - prevu);
            }
            else if (dimanche && !ferie)
            {
                dim += retenu;
            }
            else
            {
                ferieH += retenu;
            }
        }

        return new OvertimeDayResult(
            jour.Jour.Date, paires[0].EntreeHeure, paires[^1].SortieHeure,
            WorkIntervals.Arrondir(reel), WorkIntervals.Arrondir(retenuTotal),
            WorkIntervals.Arrondir(nuit), WorkIntervals.Arrondir(dim), WorkIntervals.Arrondir(ferieH),
            WorkIntervals.CalculerRetard(config, jour, paires),
            Absence: 0m,
            HeuresSup: WorkIntervals.Arrondir(hs),
            Conge: false,
            JourTravaille: true,
            NombrePaires: paires.Count);
    }
}
