using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class MultiPunchDayCalculator
{
    internal enum MajorationBase
    {
        Aucune = 0,
        Dimanche = 1,
        Ferie = 2
    }

    internal static OvertimeDayResult JourMulti(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = WorkIntervals.PauseTheorique(config, jour);
        var paires = WorkIntervals.Trier(jour.Paires);
        var weekEnd = jour.Jour.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday;

        // Congé saisi dans SAGE : la journée est neutralisée (payée comme travaillée), comme le desktop.
        if (jour.CongeValeur > 0m && !weekEnd)
            return JourConge(config, jour, paires);

        // Journée sans pointage : férié payé, 8 mars (femmes) payé, sinon rien.
        if (paires.Count == 0)
        {
            var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
            var retenu = JourneeCrediteeSansTravail(config, jour, dimanche, jour.Ferie);
            return new OvertimeDayResult(
                jour.Jour.Date, null, null, 0m, retenu,
                0m, 0m, 0m, 0m,
                WorkIntervals.Absence(jour, pause),
                0m, Conge: false, JourTravaille: false, NombrePaires: 0);
        }

        return JourTravaille(config, jour, paires, pause);
    }

    internal static decimal JourneeCrediteeSansTravail(
        OvertimeWeekConfig config, OvertimeDayInput jour, bool dimanche, bool ferie)
    {
        if (dimanche)
            return 0m;
        if (ferie)
            return config.Journee;
        if (config.AppliqueJourneeFemmes(jour.Jour))
            return config.Journee;
        return 0m;
    }

    private static OvertimeDayResult JourConge(OvertimeWeekConfig config, OvertimeDayInput jour, List<PunchPair> paires)
    {
        var intervalles = WorkIntervals.Intervalles(paires, jour.PlanningHa, config.PasSortieMinutes);
        var reel = WorkIntervals.Heures(WorkIntervals.Somme(intervalles));
        var entree = paires.Count > 0 ? paires[0].EntreeHeure : (TimeSpan?)null;
        var sortie = paires.Count > 0 ? paires[^1].SortieHeure : (TimeSpan?)null;
        return new OvertimeDayResult(
            jour.Jour.Date, entree, sortie, reel,
            WorkIntervals.Arrondir(config.Journee * jour.CongeValeur),
            0m, 0m, 0m, 0m, 0m, 0m,
            Conge: true, JourTravaille: paires.Count > 0, NombrePaires: paires.Count);
    }

    private static OvertimeDayResult JourTravaille(
        OvertimeWeekConfig config, OvertimeDayInput jour, List<PunchPair> paires, TimeSpan pause)
    {
        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var intervalles = WorkIntervals.Intervalles(paires, jour.PlanningHa, config.PasSortieMinutes);
        var dureeTravaillee = WorkIntervals.Somme(intervalles);
        var reel = WorkIntervals.Heures(dureeTravaillee);

        // Pause : seule la part manquante est déduite, et seulement au-delà de 5 h travaillées.
        var manque = ManquePause(intervalles, dureeTravaillee, pause);
        var retenuJour = reel > (decimal)OvertimeRules.PauseThreshold.TotalHours
            ? Math.Max(0m, reel - WorkIntervals.Heures(manque))
            : reel;

        var (nuit, dim, ferieH) = Majorations(config, intervalles, jour.Ferie);
        // Le dimanche est compté en heures retenues (pause déduite) ; le férié en heures réelles (desktop).
        if (dimanche)
            dim = Math.Max(0m, dim - WorkIntervals.Heures(manque));

        // 8 mars : jour férié pour les femmes, MFerie += heures retenues et complément de journée au total.
        if (config.AppliqueJourneeFemmes(jour.Jour) && !dimanche && !jour.Ferie)
        {
            ferieH += retenuJour;
            retenuJour = Math.Max(retenuJour, config.Journee);
        }

        return new OvertimeDayResult(
            jour.Jour.Date, paires[0].EntreeHeure, paires[^1].SortieHeure, reel,
            WorkIntervals.Arrondir(retenuJour),
            WorkIntervals.Arrondir(nuit), WorkIntervals.Arrondir(dim), WorkIntervals.Arrondir(ferieH),
            WorkIntervals.CalculerRetard(config, jour, paires),
            Absence: 0m, HeuresSup: 0m, Conge: false, JourTravaille: true, NombrePaires: paires.Count);
    }

    /// <summary>Part de la pause théorique non couverte par les trous entre les intervalles travaillés.</summary>
    private static TimeSpan ManquePause(
        List<WorkIntervals.Intervalle> intervalles, TimeSpan dureeTravaillee, TimeSpan pause)
    {
        var amplitude = intervalles.Count > 0
            ? intervalles[^1].Fin - intervalles[0].Debut
            : TimeSpan.Zero;
        var trous = amplitude - dureeTravaillee;
        if (trous < TimeSpan.Zero)
            trous = TimeSpan.Zero;
        var manque = pause - trous;
        return manque < TimeSpan.Zero ? TimeSpan.Zero : manque;
    }

    /// <summary>Majorations : chaque heure ne reçoit qu'une cotation, la plus forte gagne (synthèse §9-8).</summary>
    private static (decimal Nuit, decimal Dimanche, decimal Ferie) Majorations(
        OvertimeWeekConfig config, List<WorkIntervals.Intervalle> intervalles, bool ferie)
    {
        decimal nuit = 0m, dim = 0m, ferieH = 0m;
        foreach (var intervalle in intervalles)
        {
            var duree = WorkIntervals.Heures(intervalle.Duree);
            var nuitPart = WorkIntervals.Heures(WorkIntervals.NuitEntre(intervalle.Debut, intervalle.Fin));
            var baseMajoration = ferie
                ? MajorationBase.Ferie
                : intervalle.Debut.DayOfWeek == DayOfWeek.Sunday
                    ? MajorationBase.Dimanche
                    : MajorationBase.Aucune;

            var (partNuit, partMajoree) = Partager(config, baseMajoration, duree, nuitPart);
            nuit += partNuit;
            if (baseMajoration == MajorationBase.Ferie)
                ferieH += partMajoree;
            else if (baseMajoration == MajorationBase.Dimanche)
                dim += partMajoree;
        }
        return (nuit, dim, ferieH);
    }

    /// <summary>
    /// Répartit un intervalle majoré entre la nuit et sa base (dimanche, férié) : la nuit n'est
    /// isolée que si sa cotation dépasse celle de la base.
    /// </summary>
    private static (decimal Nuit, decimal Majore) Partager(
        OvertimeWeekConfig config, MajorationBase baseMajoration, decimal duree, decimal nuitPart)
    {
        if (baseMajoration == MajorationBase.Aucune)
            return (nuitPart, 0m);
        var cotationBase = baseMajoration == MajorationBase.Ferie ? config.CotationFerie : config.CotationDimanche;
        return config.CotationNuit > cotationBase
            ? (nuitPart, Math.Max(0m, duree - nuitPart))
            : (0m, duree);
    }
}
