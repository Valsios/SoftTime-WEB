using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Construction d'une journée du calcul des heures supplémentaires, partagée par l'écran HS
/// (<c>OvertimeService</c>) et la base analytique du moteur prédictif : les deux doivent voir les
/// mêmes paires, le même planning et la même notion de présence.
/// </summary>
public static class OvertimeDayRules
{
    private static readonly DateTime SentinelleHeure = new(2000, 1, 1);

    /// <summary>Le planning appartient-il à la pointeuse de la requête (ou à aucune) ?</summary>
    public static bool EstDeLaPointeuse(T_PLANNING_SAL plan, string pointeuseDb)
        => string.IsNullOrWhiteSpace(plan.BDD_POINTEUSE)
           || string.Equals(plan.BDD_POINTEUSE.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Planning retenu pour le calcul HS : le plus petit ID du jour.</summary>
    public static T_PLANNING_SAL? PlanDuJour(IEnumerable<T_PLANNING_SAL> plansDuJour)
        => plansDuJour.OrderBy(p => p.ID).FirstOrDefault();

    /// <summary>
    /// Une heure corrigée atteste-t-elle une présence ? Une ligne qui ne fait que déclarer une absence
    /// (arbitrage <c>ABSENCE_AM</c> / <c>ABSENCE_PM</c> sans aucune heure) n'en est pas une : sans
    /// cette distinction, arbitrer une anomalie en absence effaçait l'absence du calcul.
    /// </summary>
    public static bool EstPresence(T_HEURECORRIGER ligne)
        => ligne.HENTREE.HasValue
           || ligne.HSORTIE.HasValue
           || !(ligne.ABSENCE_AM == true || ligne.ABSENCE_PM == true);

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
        DateTime jour,
        IEnumerable<T_HEURECORRIGER> lignesDuJour,
        T_PLANNING_SAL? plan,
        bool ferie,
        decimal congeValeur)
    {
        var lignes = lignesDuJour
            .OrderBy(h => h.HENTREE ?? TimeSpan.Zero)
            .ToList();
        var paires = lignes
            .Where(h => h.HENTREE.HasValue && h.HSORTIE.HasValue)
            .Select(h => new PunchPair(
                h.DATE_POINTAGE_IN!.Value.Date,
                h.HENTREE!.Value,
                (h.DATE_POINTAGE_OUT ?? h.DATE_POINTAGE_IN)!.Value.Date,
                h.HSORTIE!.Value))
            .ToList();

        var off = plan?.OffP == true;
        // Les heures de planning « vides » du desktop portent une date sentinelle : elles valent NULL.
        var haPlanifiee = plan?.HA is DateTime haBrute && haBrute.Date > SentinelleHeure ? haBrute.TimeOfDay : (TimeSpan?)null;
        var hdPlanifiee = plan?.HD is DateTime hdBrute && hdBrute.Date > SentinelleHeure ? hdBrute.TimeOfDay : (TimeSpan?)null;
        TimeSpan? ha = off ? null : haPlanifiee;
        TimeSpan? hd = off ? null : hdPlanifiee;

        // Planning manquant : le desktop le reconstruit avec les heures réelles (entrée et sortie
        // non corrigées), ce qui neutralise le seuil journalier du mode 2 pointages.
        if (!off && paires.Count > 0)
        {
            ha ??= paires[0].EntreeHeure;
            hd ??= paires[^1].SortieHeure;
        }

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
            lignes.FirstOrDefault(h => h.RETARD.HasValue)?.RETARD);
    }
}
