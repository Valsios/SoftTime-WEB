using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles du calcul cantine. La règle de comptage historique du desktop
/// (<c>CantineService.CalculCantines</c> : une journée travaillée d'au moins 4 h) a été remplacée
/// dans l'application web par la règle métier « un pointage tombe dans la fenêtre du repas »,
/// porteuse de la même intention mais calculée sur <c>T_POINTAGE</c> au lieu de <c>T_HEURECORRIGER</c>.
/// La détection d'anomalie reprend le contrôle du desktop (absence complète) et l'adapte au flux
/// de données du nouveau moteur : l'anomalie n'est signalée que lorsqu'un pointage vient
/// <b>contredire</b> une absence complète — sinon chaque journée d'absence produirait une ligne.
/// Classe pure (aucune dépendance EF) afin d'être testable unitairement, sur le modèle de
/// <see cref="PunchPairingRules"/>.
/// </summary>
public static class CanteenRules
{
    /// <summary>Début de la fenêtre du repas.</summary>
    public static readonly TimeSpan DebutFenetre = new(11, 0, 0);

    /// <summary>Fin de la fenêtre du repas (borne incluse).</summary>
    public static readonly TimeSpan FinFenetre = new(14, 30, 0);

    /// <summary>Motif porté par les anomalies détectées.</summary>
    public const string MotifAbsenceContredite =
        "Absence complète (matin et après-midi) alors qu'un pointage tombe dans la fenêtre cantine";

    /// <summary>Heure dans la fenêtre cantine (bornes incluses).</summary>
    public static bool DansFenetre(TimeSpan heure) => heure >= DebutFenetre && heure <= FinFenetre;

    /// <summary>Absence complète : matin <b>et</b> après-midi déclarés absents.</summary>
    public static bool EstAbsenceComplete(bool? am, bool? pm) => am == true && pm == true;

    /// <summary>
    /// Anomalies = intersection des jours pointés dans la fenêtre cantine et des jours déclarés
    /// en absence complète, pour un même matricule. Résultat dédoublonné et trié (matricule, date).
    /// </summary>
    public static IReadOnlyList<CanteenAnomaly> DetecterAnomalies(
        IEnumerable<CanteenDay> joursAvecPointage,
        IEnumerable<CanteenDay> joursEnAbsenceComplete)
    {
        var absences = joursEnAbsenceComplete
            .Select(a => (a.Matricule, Date: a.Date.Date))
            .ToHashSet();
        return joursAvecPointage
            .Where(j => absences.Contains((j.Matricule, j.Date.Date)))
            .Select(j => new CanteenAnomaly(j.Matricule, j.Date.Date, MotifAbsenceContredite))
            .Distinct()
            .OrderBy(a => a.Matricule, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Date)
            .ToList();
    }
}
