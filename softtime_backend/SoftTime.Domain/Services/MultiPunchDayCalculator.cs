using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class MultiPunchDayCalculator
{

    // ------------------------------------------------------------------ multi-pointage

    internal static OvertimeDayResult JourMulti(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = WorkIntervals.PauseTheorique(config, jour);
        var paires = WorkIntervals.Trier(jour.Paires);
        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var ferie = jour.Ferie;
        var samedi = jour.Jour.DayOfWeek == DayOfWeek.Saturday;
        var conge = jour.CongeValeur > 0m && !dimanche && !samedi;

        var intervalles = WorkIntervals.Intervalles(paires, jour.PlanningHa, config.PasSortieMinutes);
        var dureeTravaillee = WorkIntervals.Somme(intervalles);
        var reel = WorkIntervals.Heures(dureeTravaillee);
        var entree = paires.Count > 0 ? paires[0].EntreeHeure : (TimeSpan?)null;
        var sortie = paires.Count > 0 ? paires[^1].SortieHeure : (TimeSpan?)null;

        // Congé saisi dans SAGE : la journée est neutralisée (payée comme travaillée), comme le desktop.
        if (conge)
        {
            return new OvertimeDayResult(
                jour.Jour.Date, entree, sortie, reel,
                WorkIntervals.Arrondir(config.Journee * jour.CongeValeur),
                0m, 0m, 0m, 0m, 0m, 0m,
                Conge: true, JourTravaille: paires.Count > 0, NombrePaires: paires.Count);
        }

        // Journée sans pointage : férié payé, 8 mars (femmes) payé, sinon rien.
        if (paires.Count == 0)
        {
            var retenu = JourneeCrediteeSansTravail(config, jour, dimanche, ferie);
            return new OvertimeDayResult(
                jour.Jour.Date, null, null, 0m, retenu,
                0m, 0m, 0m, 0m,
                WorkIntervals.Absence(jour, pause),
                0m, Conge: false, JourTravaille: false, NombrePaires: 0);
        }

        // Pause : seule la part manquante est déduite, et seulement au-delà de 5 h travaillées.
        var amplitude = intervalles.Count > 0
            ? intervalles[^1].Fin - intervalles[0].Debut
            : TimeSpan.Zero;
        var trous = amplitude - dureeTravaillee;
        if (trous < TimeSpan.Zero)
            trous = TimeSpan.Zero;
        var manque = pause - trous;
        if (manque < TimeSpan.Zero)
            manque = TimeSpan.Zero;

        var retenuJour = reel > (decimal)OvertimeRules.PauseThreshold.TotalHours
            ? Math.Max(0m, reel - WorkIntervals.Heures(manque))
            : reel;

        // Majorations : chaque heure ne reçoit qu'une cotation, la plus forte gagne (synthèse §9-8).
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

            switch (baseMajoration)
            {
                case MajorationBase.Ferie:
                    if (config.CotationNuit > config.CotationFerie)
                    {
                        nuit += nuitPart;
                        ferieH += Math.Max(0m, duree - nuitPart);
                    }
                    else
                    {
                        ferieH += duree;
                    }
                    break;
                case MajorationBase.Dimanche:
                    if (config.CotationNuit > config.CotationDimanche)
                    {
                        nuit += nuitPart;
                        dim += Math.Max(0m, duree - nuitPart);
                    }
                    else
                    {
                        dim += duree;
                    }
                    break;
                default:
                    nuit += nuitPart;
                    break;
            }
        }

        // Le dimanche est compté en heures retenues (pause déduite) ; le férié en heures réelles (desktop).
        if (dimanche)
            dim = Math.Max(0m, dim - WorkIntervals.Heures(manque));

        // 8 mars : jour férié pour les femmes, MFerie += heures retenues et complément de journée au total.
        if (config.AppliqueJourneeFemmes(jour.Jour) && !dimanche && !ferie)
        {
            ferieH += retenuJour;
            retenuJour = Math.Max(retenuJour, config.Journee);
        }

        return new OvertimeDayResult(
            jour.Jour.Date, entree, sortie, reel,
            WorkIntervals.Arrondir(retenuJour),
            WorkIntervals.Arrondir(nuit), WorkIntervals.Arrondir(dim), WorkIntervals.Arrondir(ferieH),
            WorkIntervals.CalculerRetard(config, jour, paires),
            Absence: 0m,
            HeuresSup: 0m,
            Conge: false,
            JourTravaille: true,
            NombrePaires: paires.Count);
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

    internal enum MajorationBase
    {
        Aucune = 0,
        Dimanche = 1,
        Ferie = 2
    }
}
