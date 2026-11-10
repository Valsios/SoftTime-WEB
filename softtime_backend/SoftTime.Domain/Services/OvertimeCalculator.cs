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

        var ht = WorkIntervals.Arrondir(resultats.Sum(r => r.HeuresRetenues));
        var hs = config.MultiPointage
            ? Math.Max(0m, WorkIntervals.Arrondir(ht - config.HeuresSemaine))
            : WorkIntervals.Arrondir(resultats.Sum(r => r.HeuresSup));

        return new OvertimeWeekResult(
            ht,
            hs,
            WorkIntervals.Arrondir(resultats.Sum(r => r.Nuit)),
            WorkIntervals.Arrondir(resultats.Sum(r => r.Dimanche)),
            WorkIntervals.Arrondir(resultats.Sum(r => r.Ferie)),
            WorkIntervals.Arrondir(resultats.Sum(r => r.Retard)),
            WorkIntervals.Arrondir(resultats.Sum(r => r.Absence)),
            resultats);
    }

    public static OvertimeDayResult CalculerJour(OvertimeWeekConfig config, OvertimeDayInput jour)
        => config.MultiPointage ? MultiPunchDayCalculator.JourMulti(config, jour) : MonoPunchDayCalculator.JourMono(config, jour);
}
