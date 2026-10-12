using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class OvertimeRules
{
    public const decimal ExemptCeilingHours = 20m;
    public const decimal WeeklyHs130Cap = 8m;

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

    public static int GetMinuteSortie(int sortie, int tol)
    {
        if (tol <= 0)
            return sortie;
        var reste = sortie;
        while (reste > tol)
            reste -= tol;
        return sortie - reste;
    }

    /// <summary>
    /// Ports RExtensions.setHSEXO130EXO150I130I150 week allocation (20h EXO ceiling).
    /// </summary>
    public static HsExoSplit AllocateExempt(IReadOnlyList<HsWeekSplit> weeks)
    {
        var result = new HsExoSplit();
        if (weeks.Count == 0)
            return result;

        decimal totExo = 0;
        for (var i = 0; i < weeks.Count; i++)
        {
            var pla = weeks[i];
            if (i == 0)
            {
                if (pla.Hs <= ExemptCeilingHours)
                {
                    result.Exo130 += pla.Hs1;
                    if (pla.Hs2 != 0)
                        result.Exo150 += pla.Hs2;
                    totExo = result.Exo130 + result.Exo150;
                }
                else
                {
                    result.Exo130 += pla.Hs1;
                    result.Exo150 += 12;
                    result.Impo150 += pla.Hs2 - 12;
                    totExo = result.Exo130 + result.Exo150;
                }
            }
            else if (pla.Hs > 0)
            {
                if (totExo < ExemptCeilingHours)
                {
                    var reste = ExemptCeilingHours - totExo;
                    if (reste > 8)
                    {
                        result.Exo130 += pla.Hs1;
                        totExo += pla.Hs1;
                        reste = ExemptCeilingHours - totExo;
                        if (pla.Hs2 > reste)
                        {
                            result.Exo150 += reste;
                            result.Impo150 += pla.Hs2 - reste;
                            totExo += reste;
                        }
                        else if (pla.Hs2 > 0)
                        {
                            result.Exo150 += pla.Hs2;
                            totExo += pla.Hs2;
                        }
                    }
                    else if (reste > 0)
                    {
                        if (reste <= pla.Hs1)
                        {
                            result.Exo130 += reste;
                            result.Impo130 += pla.Hs1 - reste;
                            result.Impo150 += pla.Hs2;
                            totExo += reste;
                        }
                        else
                        {
                            result.Exo130 += pla.Hs1;
                            totExo += pla.Hs1;
                        }
                    }
                    else
                    {
                        result.Impo130 += pla.Hs1;
                        result.Impo150 += pla.Hs2;
                    }
                }
                else
                {
                    result.Impo130 += pla.Hs1;
                    result.Impo150 += pla.Hs2;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// La plage est-elle de nuit ? Même règle que le calcul de semaine (entrée avant 6 h
    /// ou sortie à partir de 21 h).
    /// </summary>
    public static bool EstPlageNuit(TimeSpan entree, TimeSpan sortie)
        => entree.Hours < 6 || sortie.Hours >= 21;

    /// <summary>
    /// Indicateur de dépassement journalier, porté du desktop
    /// (<c>DetailHS</c> : <c>Math.Max(0, totalHeures - 8)</c>). Les heures supplémentaires
    /// réellement payées restent calculées à la semaine (voir <see cref="SplitWeekly"/>).
    /// </summary>
    public static decimal HeuresSupJournalieres(TimeSpan heures, double seuil = 8)
        => heures.TotalHours <= seuil ? 0m : (decimal)(heures.TotalHours - seuil);
}
