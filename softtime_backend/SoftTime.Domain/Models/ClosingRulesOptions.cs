namespace SoftTime.Domain.Models;

/// <summary>
/// Paramètres des règles de clôture. Les valeurs par défaut sont fixées dans le code : le Domain
/// ne lit aucune configuration, c'est à l'appelant de fournir d'autres valeurs.
/// </summary>
public sealed record ClosingRulesOptions
{
    public const int DefaultDuplicateToleranceSeconds = 30;

    public static ClosingRulesOptions Default { get; } = new();

    /// <summary>Deux pointages séparés de strictement moins de ce délai sont des quasi-doublons.</summary>
    public int DuplicateToleranceSeconds { get; init; } = DefaultDuplicateToleranceSeconds;

    /// <summary>
    /// Pointages impairs : ignorer les jours d'une semaine validée, comme le pré-traitement (défaut).
    /// À <c>false</c>, ces jours sont aussi signalés.
    /// </summary>
    public bool IgnorerSemainesValidees { get; init; } = true;

    /// <summary>
    /// Pointages d'un salarié sans AUCUNE ligne de planning (de toute nature) sur la période analysée :
    /// ignorés par la règle « pointage sans planning » (défaut). À <c>false</c>, ils sont signalés.
    /// </summary>
    public bool ExclureSansPlanningSurPeriode { get; init; } = true;

    /// <summary>Gravité des doublons (exacts et quasi) en mono-pointage, où ils peuvent fausser les heures.</summary>
    public ClosingSeverity DoublonsMonoGravite { get; init; } = ClosingSeverity.Mineure;

    public int PoidsInformation { get; init; } = (int)ClosingSeverity.Information;

    public int PoidsMineure { get; init; } = (int)ClosingSeverity.Mineure;

    public int PoidsImportante { get; init; } = (int)ClosingSeverity.Importante;

    public int PoidsCritique { get; init; } = (int)ClosingSeverity.Critique;

    /// <summary>Poids d'une gravité ; un poids négatif est ramené à 0.</summary>
    public int Poids(ClosingSeverity gravite) => Math.Max(0, gravite switch
    {
        ClosingSeverity.Information => PoidsInformation,
        ClosingSeverity.Mineure => PoidsMineure,
        ClosingSeverity.Importante => PoidsImportante,
        ClosingSeverity.Critique => PoidsCritique,
        _ => 0
    });

    public int PoidsMaximum => Math.Max(Math.Max(Poids(ClosingSeverity.Information), Poids(ClosingSeverity.Mineure)),
        Math.Max(Poids(ClosingSeverity.Importante), Poids(ClosingSeverity.Critique)));

    /// <summary>
    /// Gravités remplaçant celles par défaut (<see cref="Services.PayrollClosingRules.GraviteParDefaut"/>)
    /// pour certains types. Vide par défaut.
    /// </summary>
    public IReadOnlyDictionary<ClosingAnomalyType, ClosingSeverity> Gravites { get; init; } =
        new Dictionary<ClosingAnomalyType, ClosingSeverity>();

    /// <summary>
    /// Gravité appliquée à un type. Priorité : 1) la gravité fixée explicitement dans
    /// <see cref="Gravites"/> (dans les deux modes) ; 2) pour les doublons exacts et quasi-doublons,
    /// <see cref="ClosingSeverity.Information"/> en multi-pointage (sans effet sur la paie) et
    /// <see cref="DoublonsMonoGravite"/> en mono-pointage ; 3) la gravité par défaut du type.
    /// </summary>
    public ClosingSeverity Gravite(ClosingAnomalyType type, bool multiPointage = false)
    {
        if (Gravites.TryGetValue(type, out var gravite))
            return gravite;
        if (type is ClosingAnomalyType.DoublonExact or ClosingAnomalyType.QuasiDoublon)
            return multiPointage ? ClosingSeverity.Information : DoublonsMonoGravite;
        return Services.PayrollClosingRules.GraviteParDefaut(type);
    }

    /// <summary>Seuil du statut « Bloquant » : critiques ÷ jours-employé contrôlés (défaut provisoire 0,05).</summary>
    public decimal CriticalRatioBlocking { get; init; } = Services.ClosingStatusRules.DefaultCriticalRatioBlocking;
}
