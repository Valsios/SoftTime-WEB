using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SoftTime.Domain.Models;
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
///   "DuplicateToleranceSeconds": 120, "IgnorerSemainesValidees": true, "MaxPeriodDays": 366,
///   "CriticalRatioBlocking": 0.02,
///   "Gravites": { "QuasiDoublon": "Importante" },
///   "Poids": { "Mineure": 1, "Importante": 3, "Critique": 5 }
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

    public int MaxPeriodDays { get; set; } = PlafondPeriodDays;

    /// <summary>Seuil du statut « Bloquant » (critiques ÷ jours-employé) ; défaut PROVISOIRE à confirmer.</summary>
    public decimal CriticalRatioBlocking { get; set; } = ClosingStatusRules.DefaultCriticalRatioBlocking;

    /// <summary>Gravité imposée par type d'anomalie (les types absents gardent leur gravité par défaut).</summary>
    public Dictionary<ClosingAnomalyType, ClosingSeverity> Gravites { get; } = new();

    /// <summary>Poids du score par gravité.</summary>
    public Dictionary<ClosingSeverity, int> Poids { get; } = new()
    {
        [ClosingSeverity.Mineure] = (int)ClosingSeverity.Mineure,
        [ClosingSeverity.Importante] = (int)ClosingSeverity.Importante,
        [ClosingSeverity.Critique] = (int)ClosingSeverity.Critique
    };

    /// <summary>Clés illisibles rencontrées à la lecture de la section (signalées par le validateur).</summary>
    internal List<string> ErreursLecture { get; } = new();

    /// <summary>Options passées aux règles du Domain.</summary>
    public ClosingRulesOptions ReglesParDefaut() => new()
    {
        DuplicateToleranceSeconds = DuplicateToleranceSeconds,
        IgnorerSemainesValidees = IgnorerSemainesValidees,
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
        Entier(section, nameof(MaxPeriodDays), v => MaxPeriodDays = v);
        if (section[nameof(CriticalRatioBlocking)] is string ratio)
        {
            if (decimal.TryParse(ratio, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var valeur))
                CriticalRatioBlocking = valeur;
            else
                ErreursLecture.Add($"{Section}:{nameof(CriticalRatioBlocking)} doit être un nombre décimal (ex. 0.02).");
        }

        foreach (var ligne in section.GetSection(nameof(Gravites)).GetChildren())
        {
            if (!EnumNomme<ClosingAnomalyType>(ligne.Key, out var type))
                ErreursLecture.Add($"{Section}:{nameof(Gravites)}:{ligne.Key} : type d'anomalie inconnu.");
            else if (!EnumNomme<ClosingSeverity>(ligne.Value, out var gravite))
                ErreursLecture.Add($"{Section}:{nameof(Gravites)}:{ligne.Key} doit valoir Mineure, Importante ou Critique.");
            else
                Gravites[type] = gravite;
        }

        foreach (var ligne in section.GetSection(nameof(Poids)).GetChildren())
        {
            if (!EnumNomme<ClosingSeverity>(ligne.Key, out var gravite))
                ErreursLecture.Add($"{Section}:{nameof(Poids)}:{ligne.Key} : gravité inconnue (Mineure, Importante ou Critique).");
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

/// <summary>Refuse le démarrage quand un réglage « Closing » est invalide, avec un message clair.</summary>
public sealed class ClosingOptionsValidator : IValidateOptions<ClosingOptions>
{
    public ValidateOptionsResult Validate(string? name, ClosingOptions options)
    {
        var erreurs = options.Erreurs();
        return erreurs.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(erreurs.Select(e => "Réglage de clôture invalide : " + e));
    }
}
