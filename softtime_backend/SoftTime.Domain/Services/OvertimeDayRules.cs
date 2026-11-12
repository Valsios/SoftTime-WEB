using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Construction d'une journée du calcul des heures supplémentaires, partagée par l'écran HS
/// (<c>OvertimeService</c>) et l'atterrissage de semaine : les deux doivent voir les
/// mêmes paires, le même planning et la même notion de présence.
/// </summary>
public static class OvertimeDayRules
{
    private static readonly DateTime SentinelleHeure = new(2000, 1, 1);

    /// <summary>Le planning appartient-il à la pointeuse de la requête (ou à aucune) ?</summary>
    public static bool EstDeLaPointeuse(PlanningSalarie plan, string pointeuseDb)
        => string.IsNullOrWhiteSpace(plan.BddPointeuse)
           || string.Equals(plan.BddPointeuse.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Planning retenu pour le calcul HS : le plus petit ID du jour.</summary>
    public static PlanningSalarie? PlanDuJour(IEnumerable<PlanningSalarie> plansDuJour)
        => plansDuJour.OrderBy(p => p.Id).FirstOrDefault();

    /// <summary>
    /// Une heure corrigée atteste-t-elle une présence ? Une ligne qui ne fait que déclarer une absence
    /// (arbitrage <c>ABSENCE_AM</c> / <c>ABSENCE_PM</c> sans aucune heure) n'en est pas une : sans
    /// cette distinction, arbitrer une anomalie en absence effaçait l'absence du calcul.
    /// </summary>
    public static bool EstPresence(HeureCorrigee ligne)
        => ligne.HEntree.HasValue
           || ligne.HSortie.HasValue
           || !(ligne.AbsenceAm == true || ligne.AbsencePm == true);

    /// <summary>Règle du 8 mars : la civilité SAGE n'est utile que si la période contient un 8 mars.</summary>
    public static bool ContientHuitMars(DateTime from, DateTime to)
    {
        for (var jour = from.Date; jour <= to.Date; jour = jour.AddDays(1))
        {
            if (jour.Day == 8 && jour.Month == 3)
                return true;
        }
        return false;
    }

    /// <summary>Civilité SAGE (0 = homme) vers l'indicateur de la règle du 8 mars.</summary>
    public static bool EstFemme(byte? civilite) => civilite is > 0;

    /// <summary>Journée du calcul : paires, planning (reconstruit s'il manque), férié, congé, retard saisi.</summary>
    public static OvertimeDayInput Construire(
        CalendarDay journee, IEnumerable<HeureCorrigee> lignesDuJour, PlanningSalarie? plan)
    {
        var (jour, ferie, congeValeur) = journee;
        var lignes = lignesDuJour
            .OrderBy(h => h.HEntree ?? TimeSpan.Zero)
            .ToList();
        var paires = Paires(lignes);

        var (off, ha, hd) = HeuresPlanifiees(plan, paires);

        return new OvertimeDayInput(
            jour.Date,
            paires,
            ha,
            hd,
            off ? null : plan?.Pause,
            off,
            ferie,
            lignes.Any(EstPresence),
            congeValeur,
            lignes.FirstOrDefault(h => h.Retard.HasValue)?.Retard);
    }

    /// <summary>Paires entrée / sortie complètes des heures corrigées du jour.</summary>
    private static List<PunchPair> Paires(List<HeureCorrigee> lignes)
    {
        var paires = lignes
            .Where(h => h.HEntree.HasValue && h.HSortie.HasValue)
            .Select(h => new PunchPair(
                h.DatePointageIn!.Value.Date,
                h.HEntree!.Value,
                (h.DatePointageOut ?? h.DatePointageIn)!.Value.Date,
                h.HSortie!.Value))
            .ToList();
        return paires;
    }

    /// <summary>Heures planifiées du jour (aucune un jour OFF), reconstruites depuis les pointages si le planning manque.</summary>
    private static (bool Off, TimeSpan? Ha, TimeSpan? Hd) HeuresPlanifiees(PlanningSalarie? plan, List<PunchPair> paires)
    {
        var off = plan?.OffP == true;
        // Les heures de planning « vides » du desktop portent une date sentinelle : elles valent NULL.
        var haPlanifiee = plan?.Ha is DateTime haBrute && haBrute.Date > SentinelleHeure ? haBrute.TimeOfDay : (TimeSpan?)null;
        var hdPlanifiee = plan?.Hd is DateTime hdBrute && hdBrute.Date > SentinelleHeure ? hdBrute.TimeOfDay : (TimeSpan?)null;
        TimeSpan? ha = off ? null : haPlanifiee;
        TimeSpan? hd = off ? null : hdPlanifiee;

        // Planning manquant : le desktop le reconstruit avec les heures réelles (entrée et sortie
        // non corrigées), ce qui neutralise le seuil journalier du mode 2 pointages.
        if (!off && paires.Count > 0)
        {
            ha ??= paires[0].EntreeHeure;
            hd ??= paires[^1].SortieHeure;
        }
        return (off, ha, hd);
    }
}
