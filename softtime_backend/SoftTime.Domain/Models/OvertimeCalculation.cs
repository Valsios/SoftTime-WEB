#nullable enable

namespace SoftTime.Domain.Models;

/// <summary>
/// Paramétrage d'une semaine de calcul HS, indépendant d'EF : mode de calcul (<c>T_CLOCK.MULTIPOINT</c>),
/// heures contractuelles et pause de la catégorie (<c>T_CATEGORIE</c>), tolérances d'entrée / de sortie
/// (<c>T_TOLERANCES</c>) et priorités de majoration (<c>T_MAJORATION</c>).
/// Voir la synthèse métier desktop §3 (paramétrage) et §9 (règles à répliquer).
/// </summary>
public sealed record OvertimeWeekConfig(
    bool MultiPointage,
    decimal HeuresSemaine = 40m,
    TimeSpan? PauseCategorie = null,
    int ToleranceEntreeMinutes = 0,
    int? PasSortieMinutes = null,
    decimal CotationNuit = 0m,
    decimal CotationDimanche = 0m,
    decimal CotationFerie = 0m,
    bool EstFemme = false)
{
    /// <summary>Journée théorique : <c>HEURESEMAINE / 5</c> (synthèse §3).</summary>
    public decimal Journee => HeuresSemaine / 5m;

    /// <summary>
    /// Règle de la journée du 8 mars (jour férié pour les femmes, synthèse §4.3 d).
    /// La civilité nulle n'applique pas la règle (écart assumé avec le desktop).
    /// </summary>
    public bool AppliqueJourneeFemmes(DateTime jour)
        => EstFemme && jour.Day == 8 && jour.Month == 3;
}

/// <summary>
/// Journée soumise au calcul : paires entrée/sortie retenues (<c>T_HEURECORRIGER</c>) et contexte
/// (planning du jour, férié, congé, retard explicitement saisi).
/// </summary>
public sealed record OvertimeDayInput(
    DateTime Jour,
    IReadOnlyList<PunchPair> Paires,
    TimeSpan? PlanningHa = null,
    TimeSpan? PlanningHd = null,
    TimeSpan? PlanningPause = null,
    bool Off = false,
    bool Ferie = false,
    bool CorrectionPresente = false,
    decimal CongeValeur = 0m,
    TimeSpan? RetardExplicite = null);

/// <summary>Résultat d'une journée : heures retenues et compteurs de majoration.</summary>
public sealed record OvertimeDayResult(
    DateTime Jour,
    TimeSpan? Entree,
    TimeSpan? Sortie,
    decimal HeuresReelles,
    decimal HeuresRetenues,
    decimal Nuit,
    decimal Dimanche,
    decimal Ferie,
    decimal Retard,
    decimal Absence,
    decimal HeuresSup,
    bool Conge,
    bool JourTravaille,
    int NombrePaires);

/// <summary>
/// Résultat d'une semaine (lundi → dimanche). <see cref="Ht"/> est le total des heures créditées
/// (jours travaillés, fériés non travaillés, congés et complément du 8 mars, comme le desktop).
/// </summary>
public sealed record OvertimeWeekResult(
    decimal Ht,
    decimal Hs,
    decimal Nuit,
    decimal Dimanche,
    decimal Ferie,
    decimal Retard,
    decimal Absence,
    IReadOnlyList<OvertimeDayResult> Jours);
