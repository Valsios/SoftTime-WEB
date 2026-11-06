using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class MonoPunchDayCalculator
{
    internal static OvertimeDayResult JourMono(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = WorkIntervals.PauseTheorique(config, jour);
        var paires = WorkIntervals.Trier(jour.Paires);
        if (paires.Count == 0)
            return SansPointage(config, jour, pause);

        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var samedi = jour.Jour.DayOfWeek == DayOfWeek.Saturday;
        var prevu = WorkIntervals.PrevuJournalier(jour, paires[0], paires[^1], pause);
        var entreePlanifiee = jour.PlanningHa ?? paires[0].EntreeHeure;
        var segments = paires
            .Select(p => Segment(p, entreePlanifiee))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();
        var bruts = segments.Select(s => WorkIntervals.Heures(s.Fin - s.Debut)).ToList();
        var retenus = bruts.Select(b => Math.Max(0m, b - WorkIntervals.Heures(pause))).ToList();
        var retenuTotal = retenus.Sum();

        // Jour ouvré : nuit en simple tranche 22:00 → 05:00 et, le samedi, toutes les heures sont
        // supplémentaires (synthèse §4.2). Dimanche et férié reçoivent les heures retenues.
        var ouvre = !dimanche && !jour.Ferie;
        var nuit = ouvre ? segments.Sum(s => WorkIntervals.Heures(WorkIntervals.NuitEntre(s.Debut, s.Fin))) : 0m;
        var hs = ouvre ? retenus.Sum(r => samedi ? r : Math.Max(0m, r - prevu)) : 0m;
        var dim = dimanche && !jour.Ferie ? retenuTotal : 0m;
        var ferieH = jour.Ferie ? retenuTotal : 0m;

        return new OvertimeDayResult(
            jour.Jour.Date, paires[0].EntreeHeure, paires[^1].SortieHeure,
            WorkIntervals.Arrondir(bruts.Sum()), WorkIntervals.Arrondir(retenuTotal),
            WorkIntervals.Arrondir(nuit), WorkIntervals.Arrondir(dim), WorkIntervals.Arrondir(ferieH),
            WorkIntervals.CalculerRetard(config, jour, paires),
            Absence: 0m,
            HeuresSup: WorkIntervals.Arrondir(hs),
            Conge: false,
            JourTravaille: true,
            NombrePaires: paires.Count);
    }

    /// <summary>Journée sans pointage : congé SAGE, férié ou 8 mars crédités, sinon absence.</summary>
    private static OvertimeDayResult SansPointage(OvertimeWeekConfig config, OvertimeDayInput jour, TimeSpan pause)
    {
        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var samedi = jour.Jour.DayOfWeek == DayOfWeek.Saturday;
        var conge = jour.CongeValeur > 0m && !dimanche && !samedi;
        var retenu = conge
            ? WorkIntervals.Arrondir(config.Journee * jour.CongeValeur)
            : MultiPunchDayCalculator.JourneeCrediteeSansTravail(config, jour, dimanche, jour.Ferie);
        return new OvertimeDayResult(
            jour.Jour.Date, null, null, 0m, retenu,
            0m, 0m, 0m, 0m,
            WorkIntervals.Absence(jour, pause),
            0m, Conge: conge, JourTravaille: false, NombrePaires: 0);
    }

    /// <summary>
    /// Intervalle retenu d'une paire : l'entrée anticipée du jour est ramenée à l'heure planifiée et
    /// la sortie arrondie ; <c>null</c> si l'intervalle est vide.
    /// </summary>
    private static WorkIntervals.Intervalle? Segment(PunchPair paire, TimeSpan entreePlanifiee)
    {
        var debut = paire.EntreeDate.Date + paire.EntreeHeure;
        if (paire.EntreeDate.Date == paire.SortieDate.Date && paire.EntreeHeure < entreePlanifiee)
            debut = paire.EntreeDate.Date + entreePlanifiee;

        var sortieHeure = WorkIntervals.ArrondirSortie(paire.SortieHeure, OvertimeRules.MonoExitRoundingMinutes);
        var fin = paire.SortieDate.Date + sortieHeure;
        return fin <= debut ? null : new WorkIntervals.Intervalle(debut, fin);
    }
}
