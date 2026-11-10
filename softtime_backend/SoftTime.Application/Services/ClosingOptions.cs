using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Réglages de la clôture de paie assistée, liés à la section « Closing » si elle existe (lue par <see cref="ClosingOptionsSetup"/>) ; toutes
/// les valeurs par défaut sont dans le code. Un réglage invalide refuse le démarrage
/// (<see cref="ClosingOptionsValidator"/>). Le Domain ne lit jamais la configuration :
/// <see cref="ReglesParDefaut"/> lui transmet ces valeurs.
/// <code>
/// "Closing": {
///   "Enabled": true, "ShowNames": true, "CacheSeconds": 120, "CacheMaxSeconds": 600,
///   "DuplicateToleranceSeconds": 30, "IgnorerSemainesValidees": true, "MaxPeriodDays": 366,
///   "CriticalRatioBlocking": 0.05,
///   "ExclureSansPlanningSurPeriode": true, "DoublonsMonoGravite": "Mineure",
///   "Gravites": { "QuasiDoublon": "Importante" },
///   "Poids": { "Information": 0, "Mineure": 1, "Importante": 3, "Critique": 5 }
/// }
/// </code>
/// </summary>
public sealed class ClosingOptions
{
    public const string Section = "Closing";

    /// <summary>Plafond absolu de la période analysée (performance), bornes comprises.</summary>
    public const int PlafondPeriodDays = 366;

    /// <summary>Délai glissant par défaut (« Closing:CacheSeconds »).</summary>
    public const int DefautCacheSeconds = 120;

    /// <summary>Borne haute de <see cref="CacheMaxSeconds"/> (une heure) : au-delà, données trop anciennes.</summary>
    public const int PlafondCacheMaxSeconds = 3600;

    /// <summary>Fonction active (route d'analyse disponible).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Noms et prénoms renvoyés avec les anomalies ; à <c>false</c>, matricule seul.</summary>
    public bool ShowNames { get; set; } = true;

    public int DuplicateToleranceSeconds { get; set; } = ClosingRulesOptions.DefaultDuplicateToleranceSeconds;

    public bool IgnorerSemainesValidees { get; set; } = true;

    /// <summary>Salariés sans aucune ligne de planning sur la période : non contrôlés par « pointage sans planning ».</summary>
    public bool ExclureSansPlanningSurPeriode { get; set; } = true;

    /// <summary>Gravité des doublons en mono-pointage (en multi-pointage : Information, sauf gravité explicite).</summary>
    public ClosingSeverity DoublonsMonoGravite { get; set; } = ClosingSeverity.Mineure;

    public int MaxPeriodDays { get; set; } = PlafondPeriodDays;

    /// <summary>Seuil du statut « Bloquant » (critiques ÷ jours-employé) ; défaut PROVISOIRE à confirmer.</summary>
    public decimal CriticalRatioBlocking { get; set; } = ClosingStatusRules.DefaultCriticalRatioBlocking;

    /// <summary>Gravité imposée par type d'anomalie (les types absents gardent leur gravité par défaut).</summary>
    public Dictionary<ClosingAnomalyType, ClosingSeverity> Gravites { get; } = new();

    /// <summary>Poids du score par gravité.</summary>
    public Dictionary<ClosingSeverity, int> Poids { get; } = new()
    {
        [ClosingSeverity.Information] = (int)ClosingSeverity.Information,
        [ClosingSeverity.Mineure] = (int)ClosingSeverity.Mineure,
        [ClosingSeverity.Importante] = (int)ClosingSeverity.Importante,
        [ClosingSeverity.Critique] = (int)ClosingSeverity.Critique
    };

    /// <summary>
    /// Durée GLISSANTE de réutilisation de l'analyse (changements de page, de filtre, de tri, d'onglet) : chaque
    /// lecture avec réutilisation la renouvelle, sans dépasser <see cref="CacheMaxSeconds"/> (« Closing:CacheSeconds »,
    /// 120 s par défaut, 0 à 3600 ; 0 : aucun cache). Une clé absente garde la valeur par défaut.
    /// </summary>
    public int CacheSeconds { get; set; } = DefautCacheSeconds;

    /// <summary>
    /// Plafond ABSOLU de réutilisation, mesuré depuis le calcul d'origine (« Closing:CacheMaxSeconds », 600 s par
    /// défaut) : au-delà, la prochaine lecture recalcule, même si l'analyse est consultée sans interruption. Le délai
    /// glissant <see cref="CacheSeconds"/> ne le dépasse jamais ; à 0, l'analyse expire après le seul délai glissant.
    /// </summary>
    public int CacheMaxSeconds { get; set; } = 600;

    /// <summary>Clés illisibles rencontrées à la lecture de la section (signalées par le validateur).</summary>
    internal List<string> ErreursLecture { get; } = new();

    /// <summary>Options passées aux règles du Domain.</summary>
    public ClosingRulesOptions ReglesParDefaut() => new()
    {
        DuplicateToleranceSeconds = DuplicateToleranceSeconds,
        IgnorerSemainesValidees = IgnorerSemainesValidees,
        ExclureSansPlanningSurPeriode = ExclureSansPlanningSurPeriode,
        DoublonsMonoGravite = DoublonsMonoGravite,
        PoidsInformation = Poids[ClosingSeverity.Information],
        PoidsMineure = Poids[ClosingSeverity.Mineure],
        PoidsImportante = Poids[ClosingSeverity.Importante],
        PoidsCritique = Poids[ClosingSeverity.Critique],
        Gravites = new Dictionary<ClosingAnomalyType, ClosingSeverity>(Gravites),
        CriticalRatioBlocking = CriticalRatioBlocking
    };

    /// <summary>Liste des erreurs de réglage (vide : réglages valides).</summary>
    public IReadOnlyList<string> Erreurs()
    {
        var erreurs = new List<string>(ErreursLecture);
        if (DuplicateToleranceSeconds < 0)
            erreurs.Add($"{Section}:{nameof(DuplicateToleranceSeconds)} ne peut pas être négatif.");
        if (MaxPeriodDays < 1 || MaxPeriodDays > PlafondPeriodDays)
            erreurs.Add($"{Section}:{nameof(MaxPeriodDays)} doit être compris entre 1 et {PlafondPeriodDays}.");
        if (CacheSeconds < 0 || CacheSeconds > PlafondCacheMaxSeconds)
            erreurs.Add($"{Section}:{nameof(CacheSeconds)} doit être compris entre 0 et {PlafondCacheMaxSeconds}.");
        else if (CacheMaxSeconds > 0 && CacheSeconds > CacheMaxSeconds)
            erreurs.Add($"{Section}:{nameof(CacheSeconds)} ne peut pas dépasser {Section}:{nameof(CacheMaxSeconds)}.");
        if (CacheMaxSeconds < 0 || CacheMaxSeconds > PlafondCacheMaxSeconds)
            erreurs.Add($"{Section}:{nameof(CacheMaxSeconds)} doit être compris entre 0 et {PlafondCacheMaxSeconds}.");
        if (CriticalRatioBlocking < 0m || CriticalRatioBlocking > 1m)
            erreurs.Add($"{Section}:{nameof(CriticalRatioBlocking)} doit être compris entre 0 et 1.");
        foreach (var (gravite, poids) in Poids.OrderBy(p => p.Key))
            if (poids < 0)
                erreurs.Add($"{Section}:{nameof(Poids)}:{gravite} ne peut pas être négatif.");
        if (Poids.Values.All(p => p <= 0))
            erreurs.Add($"{Section}:{nameof(Poids)} : au moins une gravité doit avoir un poids positif.");
        return erreurs;
    }
}
