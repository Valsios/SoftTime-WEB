using System.Globalization;

using Microsoft.Extensions.Configuration;

using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Réglages de la clôture de paie assistée, liés à la section « Closing » si elle existe ; toutes
/// les valeurs par défaut sont dans le code. Un réglage invalide refuse le démarrage
/// (<see cref="ClosingOptionsValidator"/>). Le Domain ne lit jamais la configuration :
/// <see cref="ReglesParDefaut"/> lui transmet ces valeurs.
/// <code>
/// "Closing": {
///   "Enabled": true, "ShowNames": true,
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

    /// <summary>Durée de réutilisation de l'analyse pour les changements de page (0 : aucun cache). Non lue en configuration.</summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>Cache mémoire de l'analyse : porté par l'instance unique des réglages (une par processus).</summary>
    internal ClosingAnalysisCache Cache { get; } = new();

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

    /// <summary>
    /// Lit la section « Closing » (clés absentes : valeur par défaut conservée). Les valeurs ne sont
    /// jamais recopiées dans les messages d'erreur.
    /// </summary>
    public void Lire(IConfiguration section)
    {
        Booleen(section, nameof(Enabled), v => Enabled = v);
        Booleen(section, nameof(ShowNames), v => ShowNames = v);
        Entier(section, nameof(DuplicateToleranceSeconds), v => DuplicateToleranceSeconds = v);
        Booleen(section, nameof(IgnorerSemainesValidees), v => IgnorerSemainesValidees = v);
        Booleen(section, nameof(ExclureSansPlanningSurPeriode), v => ExclureSansPlanningSurPeriode = v);
        if (section[nameof(DoublonsMonoGravite)] is string monoGravite)
        {
            if (EnumNomme<ClosingSeverity>(monoGravite, out var gravite))
                DoublonsMonoGravite = gravite;
            else
                ErreursLecture.Add($"{Section}:{nameof(DoublonsMonoGravite)} doit valoir Information, Mineure, Importante ou Critique.");
        }
        Entier(section, nameof(MaxPeriodDays), v => MaxPeriodDays = v);
        if (section[nameof(CriticalRatioBlocking)] is string ratio)
        {
            if (decimal.TryParse(ratio, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var valeur))
                CriticalRatioBlocking = valeur;
            else
                ErreursLecture.Add($"{Section}:{nameof(CriticalRatioBlocking)} doit être un nombre décimal (ex. 0.05).");
        }

        foreach (var ligne in section.GetSection(nameof(Gravites)).GetChildren())
        {
            if (!EnumNomme<ClosingAnomalyType>(ligne.Key, out var type))
                ErreursLecture.Add($"{Section}:{nameof(Gravites)}:{ligne.Key} : type d'anomalie inconnu.");
            else if (!EnumNomme<ClosingSeverity>(ligne.Value, out var gravite))
                ErreursLecture.Add($"{Section}:{nameof(Gravites)}:{ligne.Key} doit valoir Information, Mineure, Importante ou Critique.");
            else
                Gravites[type] = gravite;
        }

        foreach (var ligne in section.GetSection(nameof(Poids)).GetChildren())
        {
            if (!EnumNomme<ClosingSeverity>(ligne.Key, out var gravite))
                ErreursLecture.Add($"{Section}:{nameof(Poids)}:{ligne.Key} : gravité inconnue (Information, Mineure, Importante ou Critique).");
            else if (!int.TryParse(ligne.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var poids))
                ErreursLecture.Add($"{Section}:{nameof(Poids)}:{ligne.Key} doit être un nombre entier.");
            else
                Poids[gravite] = poids;
        }
    }

    /// <summary>Liste des erreurs de réglage (vide : réglages valides).</summary>
    public IReadOnlyList<string> Erreurs()
    {
        var erreurs = new List<string>(ErreursLecture);
        if (DuplicateToleranceSeconds < 0)
            erreurs.Add($"{Section}:{nameof(DuplicateToleranceSeconds)} ne peut pas être négatif.");
        if (MaxPeriodDays < 1 || MaxPeriodDays > PlafondPeriodDays)
            erreurs.Add($"{Section}:{nameof(MaxPeriodDays)} doit être compris entre 1 et {PlafondPeriodDays}.");
        if (CriticalRatioBlocking < 0m || CriticalRatioBlocking > 1m)
            erreurs.Add($"{Section}:{nameof(CriticalRatioBlocking)} doit être compris entre 0 et 1.");
        foreach (var (gravite, poids) in Poids.OrderBy(p => p.Key))
            if (poids < 0)
                erreurs.Add($"{Section}:{nameof(Poids)}:{gravite} ne peut pas être négatif.");
        if (Poids.Values.All(p => p <= 0))
            erreurs.Add($"{Section}:{nameof(Poids)} : au moins une gravité doit avoir un poids positif.");
        return erreurs;
    }

    private void Booleen(IConfiguration section, string cle, Action<bool> affecter)
    {
        if (section[cle] is not string brut)
            return;
        if (bool.TryParse(brut, out var valeur))
            affecter(valeur);
        else
            ErreursLecture.Add($"{Section}:{cle} doit valoir true ou false.");
    }

    private void Entier(IConfiguration section, string cle, Action<int> affecter)
    {
        if (section[cle] is not string brut)
            return;
        if (int.TryParse(brut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valeur))
            affecter(valeur);
        else
            ErreursLecture.Add($"{Section}:{cle} doit être un nombre entier.");
    }

    /// <summary>Valeur d'énumération donnée par son NOM (un nombre est refusé).</summary>
    private static bool EnumNomme<T>(string? brut, out T valeur) where T : struct, Enum
    {
        valeur = default;
        return !string.IsNullOrWhiteSpace(brut)
               && !char.IsDigit(brut.Trim()[0]) && brut.Trim()[0] != '-'
               && Enum.TryParse(brut.Trim(), ignoreCase: true, out valeur)
               && Enum.IsDefined(valeur);
    }
}
