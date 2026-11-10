using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Atterrissage d'une semaine : heures supplémentaires attendues le dimanche soir, calculées à partir
/// de ce qui est déjà pointé et du planning des jours restants. Les jours restants sont rejoués
/// « conformément au planning » dans <see cref="OvertimeCalculator"/> : congés, fériés, pause et mode
/// de pointage suivent donc exactement les règles de la validation de semaine.
///
/// L'écart habituel du salarié à son planning (<see cref="WeekDrift"/>) est une simple moyenne de
/// ses dernières semaines pointées ; il s'applique au prorata des heures encore planifiées.
/// Classe pure, sans dépendance, sur le modèle d'<see cref="OvertimeCalculator"/>.
/// </summary>
public static class WeekLandingRules
{
    /// <summary>Atterrissage d'une semaine dont les jours antérieurs à <paramref name="observeJusquA"/> sont pointés.</summary>
    public static WeekLandingResult Calculer(
        OvertimeWeekConfig config, IEnumerable<OvertimeDayInput> semaine, DateTime observeJusquA, WeekDrift derive)
    {
        var jours = semaine.OrderBy(j => j.Jour.Date).ToList();
        var observes = jours.Where(j => j.Jour.Date < observeJusquA.Date).ToList();
        var restants = jours.Where(j => j.Jour.Date >= observeJusquA.Date).ToList();

        var acquis = OvertimeCalculator.CalculerSemaine(config, observes);
        var planning = OvertimeCalculator.CalculerSemaine(config, observes.Concat(restants.Select(Planifier)));
        var reste = HeuresPlanifiees(config, restants);
        var deriveAppliquee = DeriveAppliquee(config, derive, reste, HeuresPlanifiees(config, jours));

        var tendance = config.MultiPointage
            ? planning.Ht + deriveAppliquee - config.HeuresSemaine
            : planning.Hs + deriveAppliquee;
        // Les heures déjà acquises ne se perdent pas : l'atterrissage ne peut pas passer dessous.
        var atterrissage = Math.Max(acquis.Hs, WorkIntervals.Arrondir(tendance));
        var (niveau, seuil) = Niveau(atterrissage);
        var dejaFranchi = niveau != LandingLevel.Aucun && acquis.Hs > seuil;

        return new WeekLandingResult(
            acquis.Ht,
            acquis.Hs,
            reste,
            restants.Count(EstSansPlanning),
            planning.Hs,
            deriveAppliquee,
            atterrissage,
            niveau,
            seuil,
            dejaFranchi ? 0m : Math.Min(reste, atterrissage - seuil),
            dejaFranchi);
    }

    /// <summary>
    /// Écart habituel au planning, moyenne des semaines fournies. Une semaine sans pointage ou sans
    /// planning ne dit rien de cet écart : elle est ignorée.
    /// </summary>
    public static WeekDrift Deriver(
        OvertimeWeekConfig config, IEnumerable<IReadOnlyList<OvertimeDayInput>> semainesPointees)
    {
        var ecarts = new List<(decimal Heures, decimal HeuresSup)>();
        foreach (var semaine in semainesPointees)
        {
            if (!semaine.Any(j => j.Paires.Count > 0) || HeuresPlanifiees(config, semaine) <= 0m)
                continue;
            var reel = OvertimeCalculator.CalculerSemaine(config, semaine);
            var prevu = OvertimeCalculator.CalculerSemaine(config, semaine.Select(Planifier));
            ecarts.Add((reel.Ht - prevu.Ht, reel.Hs - prevu.Hs));
        }

        return ecarts.Count == 0
            ? WeekDrift.Aucune
            : new WeekDrift(
                WorkIntervals.Arrondir(ecarts.Average(e => e.Heures)),
                WorkIntervals.Arrondir(ecarts.Average(e => e.HeuresSup)),
                ecarts.Count);
    }

    /// <summary>Seuil le plus élevé franchi par un total hebdomadaire d'heures supplémentaires.</summary>
    public static (LandingLevel Niveau, decimal Seuil) Niveau(decimal heuresSup)
    {
        if (heuresSup > OvertimeRules.ExemptCeilingHours)
            return (LandingLevel.Plafond, OvertimeRules.ExemptCeilingHours);
        if (heuresSup > OvertimeRules.WeeklyHs130Cap)
            return (LandingLevel.Majoration150, OvertimeRules.WeeklyHs130Cap);
        return heuresSup > 0m ? (LandingLevel.HeuresSup, 0m) : (LandingLevel.Aucun, 0m);
    }

    /// <summary>
    /// La journée telle que le planning la prévoit : une seule paire, de l'heure d'arrivée à l'heure
    /// de départ planifiées. Une journée sans horaire exploitable est rendue telle quelle.
    /// </summary>
    public static OvertimeDayInput Planifier(OvertimeDayInput jour)
    {
        if (!EstPlanifiable(jour))
            return jour;

        var (ha, hd) = (jour.PlanningHa!.Value, jour.PlanningHd!.Value);
        // Poste de nuit : un départ avant l'arrivée tombe le lendemain.
        var sortie = hd < ha ? jour.Jour.Date.AddDays(1) : jour.Jour.Date;
        return jour with
        {
            Paires = new[] { new PunchPair(jour.Jour.Date, ha, sortie, hd) },
            CorrectionPresente = true,
            RetardExplicite = null
        };
    }

    /// <summary>Heures que le planning crédite sur les journées fournies (pause déduite).</summary>
    private static decimal HeuresPlanifiees(OvertimeWeekConfig config, IEnumerable<OvertimeDayInput> jours)
        => WorkIntervals.Arrondir(jours
            .Where(EstPlanifiable)
            .Sum(j => OvertimeCalculator.CalculerJour(config, Planifier(j)).HeuresRetenues));

    /// <summary>Écart habituel ramené aux heures encore planifiées (rien à appliquer une fois la semaine finie).</summary>
    private static decimal DeriveAppliquee(OvertimeWeekConfig config, WeekDrift derive, decimal reste, decimal total)
    {
        if (reste <= 0m || total <= 0m)
            return 0m;
        var hebdomadaire = config.MultiPointage ? derive.Heures : derive.HeuresSup;
        return WorkIntervals.Arrondir(hebdomadaire * reste / total);
    }

    /// <summary>Jour travaillé prévu par le planning : ni repos, ni férié, ni congé, avec un horaire non vide.</summary>
    private static bool EstPlanifiable(OvertimeDayInput jour)
        => EstOuvrable(jour)
           && jour.PlanningHa.HasValue
           && jour.PlanningHd.HasValue
           && jour.PlanningHa != jour.PlanningHd;

    /// <summary>Jour de semaine où un planning était attendu et n'a pas été saisi.</summary>
    private static bool EstSansPlanning(OvertimeDayInput jour)
        => EstOuvrable(jour)
           && jour.Jour.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
           && !(jour.PlanningHa.HasValue && jour.PlanningHd.HasValue);

    private static bool EstOuvrable(OvertimeDayInput jour)
        => !jour.Off && !jour.Ferie && jour.CongeValeur <= 0m;
}
