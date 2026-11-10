using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class OvertimeRules
{
    public const decimal ExemptCeilingHours = 20m;
    public const decimal WeeklyHs130Cap = 8m;
    public const decimal DefaultWeeklyHours = 40m;

    /// <summary>Début de la plage de nuit (synthèse §4.3 d, §9-7).</summary>
    public static readonly TimeSpan NightStart = new(22, 0, 0);

    /// <summary>Fin de la plage de nuit : 05:00 est la frontière d'attribution au jour suivant.</summary>
    public static readonly TimeSpan NightEnd = new(5, 0, 0);

    /// <summary>Seuil au-delà duquel la pause manquante est déduite (mode multi, synthèse §4.3 d).</summary>
    public static readonly TimeSpan PauseThreshold = new(5, 0, 0);

    /// <summary>Pause de 30 minutes appliquée quand ni le planning ni la catégorie n'en définissent (synthèse §3).</summary>
    public static readonly TimeSpan DefaultPause = new(0, 30, 0);

    /// <summary>Pas d'arrondi de la sortie en mode 2 pointages : demi-heure inférieure (synthèse §4.4).</summary>
    public const int MonoExitRoundingMinutes = 30;

    public static HsWeekSplit SplitWeekly(decimal hs)
    {
        if (hs <= 0)
            return new HsWeekSplit();

        if (hs > WeeklyHs130Cap)
            return new HsWeekSplit { Hs = hs, Hs1 = WeeklyHs130Cap, Hs2 = hs - WeeklyHs130Cap };

        return new HsWeekSplit { Hs = hs, Hs1 = hs, Hs2 = 0 };
    }

    public static bool IsMondayToSundayWeek(DateTime start, DateTime end)
    {
        start = start.Date;
        end = end.Date;
        return start.DayOfWeek == DayOfWeek.Monday
               && end.DayOfWeek == DayOfWeek.Sunday
               && (end - start).TotalDays == 6;
    }

    public static bool IsMaxOneMonth(DateTime start, DateTime end)
    {
        start = start.Date;
        end = end.Date;
        if (end < start)
            return false;

        return end <= start.AddMonths(1);
    }

    /// <summary>
    /// Minute de sortie arrondie <b>vers le bas</b> au multiple du pas de tolérance (synthèse §3, §4.3 d).
    /// L'implémentation du desktop (<c>while (reste &gt; tol) reste -= tol</c>) retirait un pas de trop
    /// lorsque la minute était déjà un multiple exact (17:45 avec un pas de 15 devenait 17:30) : le
    /// plancher est ici arithmétique, ce que le jeu de tests de la synthèse (cas N) suppose.
    /// </summary>
    public static int GetMinuteSortie(int sortie, int tol)
    {
        if (tol <= 0 || sortie <= 0)
            return sortie;
        return sortie / tol * tol;
    }

    /// <summary>
    /// Ports RExtensions.setHSEXO130EXO150I130I150 week allocation (20h EXO ceiling).
    /// </summary>
    public static HsExoSplit AllocateExempt(IReadOnlyList<HsWeekSplit> weeks)
    {
        var result = new HsExoSplit();
        if (weeks.Count == 0)
            return result;

        var totExo = AllocateFirstWeek(result, weeks[0]);
        foreach (var pla in weeks.Skip(1).Where(w => w.Hs > 0))
            totExo = AllocateFollowingWeek(result, pla, totExo);
        return result;
    }

    /// <summary>Première semaine : au-delà du plafond, seules 12 h à 150 % restent exonérées.</summary>
    private static decimal AllocateFirstWeek(HsExoSplit result, HsWeekSplit pla)
    {
        result.Exo130 += pla.Hs1;
        if (pla.Hs <= ExemptCeilingHours)
        {
            if (pla.Hs2 != 0)
                result.Exo150 += pla.Hs2;
        }
        else
        {
            result.Exo150 += 12;
            result.Impo150 += pla.Hs2 - 12;
        }
        return result.Exo130 + result.Exo150;
    }

    /// <summary>
    /// Semaines suivantes : le reliquat du plafond est consommé d'abord à 130 % puis à 150 % ; ce qui
    /// dépasse est imposable. Comportement du desktop conservé à l'identique, y compris quand le
    /// reliquat (8 h ou moins) dépasse les heures à 130 % : les heures à 150 % ne sont alors pas reportées.
    /// </summary>
    private static decimal AllocateFollowingWeek(HsExoSplit result, HsWeekSplit pla, decimal totExo)
    {
        var reste = ExemptCeilingHours - totExo;
        if (totExo >= ExemptCeilingHours || reste <= 0)
        {
            result.Impo130 += pla.Hs1;
            result.Impo150 += pla.Hs2;
            return totExo;
        }
        if (reste <= 8)
        {
            if (reste > pla.Hs1)
            {
                result.Exo130 += pla.Hs1;
                return totExo + pla.Hs1;
            }
            result.Exo130 += reste;
            result.Impo130 += pla.Hs1 - reste;
            result.Impo150 += pla.Hs2;
            return totExo + reste;
        }

        return AllocateAboveEight(result, pla, totExo);
    }

    /// <summary>Reliquat supérieur à 8 h : toutes les heures à 130 % sont exonérées, puis celles à 150 % jusqu'au plafond.</summary>
    private static decimal AllocateAboveEight(HsExoSplit result, HsWeekSplit pla, decimal totExo)
    {
        decimal reste;
        result.Exo130 += pla.Hs1;
        totExo += pla.Hs1;
        reste = ExemptCeilingHours - totExo;
        if (pla.Hs2 > reste)
        {
            result.Exo150 += reste;
            result.Impo150 += pla.Hs2 - reste;
            return totExo + reste;
        }
        if (pla.Hs2 > 0)
        {
            result.Exo150 += pla.Hs2;
            totExo += pla.Hs2;
        }
        return totExo;
    }

    /// <summary>
    /// Indicateur de dépassement journalier, porté du desktop
    /// (<c>DetailHS</c> : <c>Math.Max(0, totalHeures - journee)</c>). Les heures supplémentaires
    /// réellement payées restent calculées à la semaine (voir <see cref="SplitWeekly"/>).
    /// </summary>
    public static decimal HeuresSupJournalieres(TimeSpan heures, double seuil = 8)
        => heures.TotalHours <= seuil ? 0m : (decimal)(heures.TotalHours - seuil);
}
