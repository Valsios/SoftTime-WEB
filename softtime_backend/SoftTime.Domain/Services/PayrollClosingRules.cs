using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles pures de contrôle avant clôture de paie : chaque règle prend des listes déjà chargées et
/// renvoie des <see cref="ClosingAnomaly"/> triées de façon déterministe (date, matricule, type,
/// détail). Aucun accès base, aucune configuration lue.
///
/// Les règles existantes sont réutilisées plutôt que recodées :
/// <list type="bullet">
///   <item><see cref="AbsenceReportRules"/> pour les jours planifiés sans pointage (sélection du
///     planning, week-ends, fériés, OFF, congés, jours futurs) et la durée planifiée ;</item>
///   <item><see cref="PunchDayGrouping"/> et <see cref="PunchPairingRules"/> pour la parité des
///     pointages, comme le pré-traitement des pointages ;</item>
///   <item><see cref="MatriculeIdentity.Cle"/> pour identifier un salarié.</item>
/// </list>
///
/// Invariant « Pause » : une ligne de planning sans heures (HA = HD, shift Pause) n'est jamais une
/// absence et ne génère jamais d'anomalie de planning (<see cref="AbsenceReportRules.CalculerDureeHeures"/>
/// renvoie <c>null</c>).
/// </summary>
public static class PayrollClosingRules
{
    /// <summary>
    /// Formule du score de clôture, réutilisable telle quelle par l'écran :
    /// <c>100 − 100 × Σ poids ÷ (poids maximum × jours-employé contrôlés)</c>, bornée à [0, 100] et
    /// arrondie à l'entier ; aucun score quand aucun jour-employé n'est contrôlé.
    /// </summary>
    public const string FormuleScore =
        "Score = 100 − 100 × (somme des poids des anomalies) ÷ (poids maximum × jours-employé contrôlés), "
        + "borné entre 0 et 100 et arrondi à l'entier. Sans jour-employé contrôlé, aucun score n'est calculé.";

    /// <summary>
    /// Gravité par défaut de chaque type d'anomalie. Les doublons (exacts et quasi) dépendent aussi du
    /// mode de pointage : voir <see cref="ClosingRulesOptions.Gravite(ClosingAnomalyType, bool)"/>.
    /// </summary>
    public static ClosingSeverity GraviteParDefaut(ClosingAnomalyType type) => type switch
    {
        ClosingAnomalyType.PointageIncomplet => ClosingSeverity.Critique,
        ClosingAnomalyType.PointageManquant => ClosingSeverity.Critique,
        ClosingAnomalyType.PlanningSansPointage => ClosingSeverity.Importante,
        ClosingAnomalyType.PointageSansPlanning => ClosingSeverity.Importante,
        ClosingAnomalyType.FerieNonApplique => ClosingSeverity.Information,
        ClosingAnomalyType.DoublonExact => ClosingSeverity.Mineure,
        ClosingAnomalyType.QuasiDoublon => ClosingSeverity.Mineure,
        _ => ClosingSeverity.Critique
    };

    /// <summary>
    /// 3.9 — Score de clôture sur 100 (voir <see cref="FormuleScore"/>) :
    /// <c>100 − 100 × Σ poids ÷ (poids maximum × joursEmployeControles)</c>, borné à [0, 100],
    /// arrondi à l'entier (demi vers le haut). Renvoie <c>null</c> sans jour-employé contrôlé, ou
    /// quand tous les poids sont nuls (score non calculable).
    /// </summary>
    public static int? Score(
        IReadOnlyList<ClosingAnomaly> anomalies,
        int joursEmployeControles,
        ClosingRulesOptions? options = null)
    {
        options ??= ClosingRulesOptions.Default;
        if (joursEmployeControles <= 0 || options.PoidsMaximum <= 0)
            return null;

        var sommePoids = anomalies.Sum(a => (decimal)options.Poids(a.Severity));
        var score = 100m - 100m * sommePoids / (options.PoidsMaximum * (decimal)joursEmployeControles);
        score = Math.Clamp(score, 0m, 100m);
        return (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Applique les gravités configurées (<see cref="ClosingRulesOptions.Gravite"/>) aux anomalies ;
    /// l'ordre est conservé (la gravité ne fait pas partie du tri). Sans gravité configurée, la liste
    /// est renvoyée telle quelle.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> AppliquerGravites(IReadOnlyList<ClosingAnomaly> anomalies, ClosingRulesOptions options)
    {
        if (options.Gravites.Count == 0)
            return anomalies;
        // Seuls les types fixés explicitement sont remplacés : la gravité des autres (dont celle des
        // doublons, qui dépend du mode) a déjà été posée par la règle.
        return anomalies
            .Select(a => options.Gravites.TryGetValue(a.Type, out var gravite) && gravite != a.Severity ? a with { Severity = gravite } : a)
            .ToList();
    }

    /// <summary>Ordre déterministe : date, matricule, type, puis détail.</summary>
    public static IReadOnlyList<ClosingAnomaly> Trier(IEnumerable<ClosingAnomaly> anomalies) =>
        anomalies
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Matricule, StringComparer.Ordinal)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Detail, StringComparer.Ordinal)
            .ToList();
}
