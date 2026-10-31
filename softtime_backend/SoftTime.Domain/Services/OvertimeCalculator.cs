#nullable enable

using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Calcul des heures supplémentaires d'une journée et d'une semaine, porté du desktop
/// (<c>Frm_Validations.calculHSSemaine</c>) et de la synthèse métier <c>SYNTHESE_CALCUL_HS.md</c> (§4, §9).
///
/// Deux modes, choisis par <c>T_CLOCK.MULTIPOINT</c> (synthèse §4.4) :
/// <list type="bullet">
///   <item><b>multi-pointage</b> (mode de référence) : HS = total hebdomadaire crédité − <c>HEURESEMAINE</c>,
///     pause déduite seulement pour sa part manquante et au-delà de 5 h travaillées, sortie arrondie au pas
///     de tolérance, nuit 22:00 → 05:00 arbitrée par <c>T_MAJORATION.Cotation</c> ;</item>
///   <item><b>2 pointages</b> : HS = somme des dépassements journaliers par rapport au planning, samedi
///     intégralement en HS, pause de catégorie toujours déduite, sortie arrondie à la demi-heure inférieure.</item>
/// </list>
///
/// Classe pure (aucune dépendance EF) afin d'être testable unitairement, sur le modèle de
/// <see cref="LeaveRules"/> et <see cref="PunchPairingRules"/>.
/// </summary>
public static class OvertimeCalculator
{
    public static OvertimeWeekResult CalculerSemaine(OvertimeWeekConfig config, IEnumerable<OvertimeDayInput>? jours)
    {
        var resultats = (jours ?? Array.Empty<OvertimeDayInput>())
            .OrderBy(j => j.Jour.Date)
            .Select(j => CalculerJour(config, j))
            .ToList();

        var ht = Arrondir(resultats.Sum(r => r.HeuresRetenues));
        var hs = config.MultiPointage
            ? Math.Max(0m, Arrondir(ht - config.HeuresSemaine))
            : Arrondir(resultats.Sum(r => r.HeuresSup));

        return new OvertimeWeekResult(
            ht,
            hs,
            Arrondir(resultats.Sum(r => r.Nuit)),
            Arrondir(resultats.Sum(r => r.Dimanche)),
            Arrondir(resultats.Sum(r => r.Ferie)),
            Arrondir(resultats.Sum(r => r.Retard)),
            Arrondir(resultats.Sum(r => r.Absence)),
            resultats);
    }

    public static OvertimeDayResult CalculerJour(OvertimeWeekConfig config, OvertimeDayInput jour)
        => config.MultiPointage ? JourMulti(config, jour) : JourMono(config, jour);

    // ------------------------------------------------------------------ multi-pointage

    private static OvertimeDayResult JourMulti(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = PauseTheorique(config, jour);
        var paires = Trier(jour.Paires);
        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var ferie = jour.Ferie;
        var samedi = jour.Jour.DayOfWeek == DayOfWeek.Saturday;
        var conge = jour.CongeValeur > 0m && !dimanche && !samedi;

        var intervalles = Intervalles(paires, jour.PlanningHa, config.PasSortieMinutes);
        var dureeTravaillee = Somme(intervalles);
        var reel = Heures(dureeTravaillee);
        var entree = paires.Count > 0 ? paires[0].EntreeHeure : (TimeSpan?)null;
        var sortie = paires.Count > 0 ? paires[^1].SortieHeure : (TimeSpan?)null;

        // Congé saisi dans SAGE : la journée est neutralisée (payée comme travaillée), comme le desktop.
        if (conge)
        {
            return new OvertimeDayResult(
                jour.Jour.Date, entree, sortie, reel,
                Arrondir(config.Journee * jour.CongeValeur),
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
                Absence(jour, pause),
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
            ? Math.Max(0m, reel - Heures(manque))
            : reel;

        // Majorations : chaque heure ne reçoit qu'une cotation, la plus forte gagne (synthèse §9-8).
        decimal nuit = 0m, dim = 0m, ferieH = 0m;
        foreach (var intervalle in intervalles)
        {
            var duree = Heures(intervalle.Duree);
            var nuitPart = Heures(NuitEntre(intervalle.Debut, intervalle.Fin));
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
            dim = Math.Max(0m, dim - Heures(manque));

        // 8 mars : jour férié pour les femmes, MFerie += heures retenues et complément de journée au total.
        if (config.AppliqueJourneeFemmes(jour.Jour) && !dimanche && !ferie)
        {
            ferieH += retenuJour;
            retenuJour = Math.Max(retenuJour, config.Journee);
        }

        return new OvertimeDayResult(
            jour.Jour.Date, entree, sortie, reel,
            Arrondir(retenuJour),
            Arrondir(nuit), Arrondir(dim), Arrondir(ferieH),
            CalculerRetard(config, jour, paires),
            Absence: 0m,
            HeuresSup: 0m,
            Conge: false,
            JourTravaille: true,
            NombrePaires: paires.Count);
    }

    private static decimal JourneeCrediteeSansTravail(
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

    // ------------------------------------------------------------------ 2 pointages

    private static OvertimeDayResult JourMono(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = PauseTheorique(config, jour);
        var paires = Trier(jour.Paires);
        var dimanche = jour.Jour.DayOfWeek == DayOfWeek.Sunday;
        var ferie = jour.Ferie;
        var samedi = jour.Jour.DayOfWeek == DayOfWeek.Saturday;
        var conge = jour.CongeValeur > 0m && !dimanche && !samedi;

        if (paires.Count == 0)
        {
            var retenu = conge
                ? Arrondir(config.Journee * jour.CongeValeur)
                : JourneeCrediteeSansTravail(config, jour, dimanche, ferie);
            return new OvertimeDayResult(
                jour.Jour.Date, null, null, 0m, retenu,
                0m, 0m, 0m, 0m,
                Absence(jour, pause),
                0m, Conge: conge, JourTravaille: false, NombrePaires: 0);
        }

        var prevu = PrevuJournalier(jour, paires[0], paires[^1], pause);
        var entreePlanifiee = jour.PlanningHa ?? paires[0].EntreeHeure;

        decimal reel = 0m, retenuTotal = 0m, nuit = 0m, dim = 0m, ferieH = 0m, hs = 0m;
        foreach (var paire in paires)
        {
            var debut = paire.EntreeDate.Date + paire.EntreeHeure;
            if (paire.EntreeDate.Date == paire.SortieDate.Date && paire.EntreeHeure < entreePlanifiee)
            {
                debut = paire.EntreeDate.Date + entreePlanifiee;
            }

            var sortieHeure = ArrondirSortie(paire.SortieHeure, OvertimeRules.MonoExitRoundingMinutes);
            var fin = paire.SortieDate.Date + sortieHeure;
            if (fin <= debut)
                continue;

            var brut = Heures(fin - debut);
            var retenu = Math.Max(0m, brut - Heures(pause));
            reel += brut;
            retenuTotal += retenu;

            if (!dimanche && !ferie)
            {
                // Nuit : simple tranche 22:00 → 05:00 (synthèse §4.2).
                nuit += Heures(NuitEntre(debut, fin));
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
            Arrondir(reel), Arrondir(retenuTotal),
            Arrondir(nuit), Arrondir(dim), Arrondir(ferieH),
            CalculerRetard(config, jour, paires),
            Absence: 0m,
            HeuresSup: Arrondir(hs),
            Conge: false,
            JourTravaille: true,
            NombrePaires: paires.Count);
    }

    // ------------------------------------------------------------------ communs

    private static decimal Arrondir(decimal heures) => Math.Round(heures, 2);

    private static TimeSpan Somme(IEnumerable<Intervalle> intervalles)
    {
        var total = TimeSpan.Zero;
        foreach (var intervalle in intervalles)
            total += intervalle.Duree;
        return total;
    }

    private static decimal Heures(TimeSpan duree) => Arrondir((decimal)duree.TotalHours);

    private static TimeSpan PauseTheorique(OvertimeWeekConfig config, OvertimeDayInput jour)
    {
        var pause = jour.PlanningPause ?? config.PauseCategorie ?? OvertimeRules.DefaultPause;
        return pause < TimeSpan.Zero ? TimeSpan.Zero : pause;
    }

    private static List<PunchPair> Trier(IReadOnlyList<PunchPair>? paires)
        => (paires ?? Array.Empty<PunchPair>())
            .OrderBy(p => p.EntreeDate.Date)
            .ThenBy(p => p.EntreeHeure)
            .ToList();

    /// <summary>
    /// Durée prévue d'une journée en mode 2 pointages : <c>(HD − HA) − pause</c>. Quand le planning
    /// manque, il est reconstruit avec les heures réelles, comme le desktop (<c>planning = new T_PLANNING_SAL()</c>),
    /// d'où l'utilisation des dates des paires pour un poste de nuit.
    /// </summary>
    private static decimal PrevuJournalier(
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
    private static decimal Absence(OvertimeDayInput jour, TimeSpan pause)
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
    private static decimal CalculerRetard(OvertimeWeekConfig config, OvertimeDayInput jour, IReadOnlyList<PunchPair> paires)
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
    private static List<Intervalle> Intervalles(
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
    private static TimeSpan NuitEntre(DateTime debut, DateTime fin)
        => Intersection(debut, fin, debut.Date, TimeSpan.Zero, OvertimeRules.NightEnd)
           + Intersection(debut, fin, debut.Date, OvertimeRules.NightStart, TimeSpan.FromDays(1));

    private static TimeSpan Intersection(DateTime debut, DateTime fin, DateTime jour, TimeSpan de, TimeSpan a)
    {
        var borneDebut = jour + de;
        var borneFin = jour + a;
        var d = debut > borneDebut ? debut : borneDebut;
        var f = fin < borneFin ? fin : borneFin;
        return f > d ? f - d : TimeSpan.Zero;
    }

    private readonly record struct Intervalle(DateTime Debut, DateTime Fin)
    {
        public TimeSpan Duree => Fin - Debut;
    }

    private enum MajorationBase
    {
        Aucune = 0,
        Dimanche = 1,
        Ferie = 2
    }
}
