using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace SoftTime.Application.Services;

/// <summary>
/// Lit la section « Closing » dans <see cref="ClosingOptions"/> (clés absentes : valeur par défaut conservée).
/// Une clé illisible est notée dans <see cref="ClosingOptions.ErreursLecture"/> et refuse le démarrage via
/// <see cref="ClosingOptionsValidator"/>. Les valeurs ne sont jamais recopiées dans les messages d'erreur.
/// </summary>
public sealed class ClosingOptionsSetup : IConfigureOptions<ClosingOptions>
{
    private const string Section = ClosingOptions.Section;

    private readonly IConfiguration _configuration;

    public ClosingOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(ClosingOptions options)
    {
        var section = _configuration.GetSection(Section);
        LireScalaires(section, options);
        LireGravites(section, options);
        LirePoids(section, options);
    }

    private static void LireScalaires(IConfiguration section, ClosingOptions options)
    {
        Booleen(section, nameof(ClosingOptions.Enabled), options, v => options.Enabled = v);
        Booleen(section, nameof(ClosingOptions.ShowNames), options, v => options.ShowNames = v);
        Entier(section, nameof(ClosingOptions.DuplicateToleranceSeconds), options, v => options.DuplicateToleranceSeconds = v);
        Booleen(section, nameof(ClosingOptions.IgnorerSemainesValidees), options, v => options.IgnorerSemainesValidees = v);
        Booleen(section, nameof(ClosingOptions.ExclureSansPlanningSurPeriode), options,
            v => options.ExclureSansPlanningSurPeriode = v);
        if (section[nameof(ClosingOptions.DoublonsMonoGravite)] is string monoGravite)
        {
            if (EnumNomme<ClosingSeverity>(monoGravite, out var gravite))
                options.DoublonsMonoGravite = gravite;
            else
                options.ErreursLecture.Add(
                    $"{Section}:{nameof(ClosingOptions.DoublonsMonoGravite)} doit valoir Information, Mineure, Importante ou Critique.");
        }
        Entier(section, nameof(ClosingOptions.MaxPeriodDays), options, v => options.MaxPeriodDays = v);
        LireRatio(section, options);
    }

    private static void LireRatio(IConfiguration section, ClosingOptions options)
    {
        if (section[nameof(ClosingOptions.CriticalRatioBlocking)] is not string ratio)
            return;
        const NumberStyles styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign
            | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (decimal.TryParse(ratio, styles, CultureInfo.InvariantCulture, out var valeur))
            options.CriticalRatioBlocking = valeur;
        else
            options.ErreursLecture.Add(
                $"{Section}:{nameof(ClosingOptions.CriticalRatioBlocking)} doit être un nombre décimal (ex. 0.05).");
    }

    private static void LireGravites(IConfiguration section, ClosingOptions options)
    {
        const string cle = nameof(ClosingOptions.Gravites);
        foreach (var ligne in section.GetSection(cle).GetChildren())
        {
            if (!EnumNomme<ClosingAnomalyType>(ligne.Key, out var type))
                options.ErreursLecture.Add($"{Section}:{cle}:{ligne.Key} : type d'anomalie inconnu.");
            else if (!EnumNomme<ClosingSeverity>(ligne.Value, out var gravite))
                options.ErreursLecture.Add(
                    $"{Section}:{cle}:{ligne.Key} doit valoir Information, Mineure, Importante ou Critique.");
            else
                options.Gravites[type] = gravite;
        }
    }

    private static void LirePoids(IConfiguration section, ClosingOptions options)
    {
        const string cle = nameof(ClosingOptions.Poids);
        foreach (var ligne in section.GetSection(cle).GetChildren())
        {
            if (!EnumNomme<ClosingSeverity>(ligne.Key, out var gravite))
                options.ErreursLecture.Add(
                    $"{Section}:{cle}:{ligne.Key} : gravité inconnue (Information, Mineure, Importante ou Critique).");
            else if (!int.TryParse(ligne.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var poids))
                options.ErreursLecture.Add($"{Section}:{cle}:{ligne.Key} doit être un nombre entier.");
            else
                options.Poids[gravite] = poids;
        }
    }

    private static void Booleen(IConfiguration section, string cle, ClosingOptions options, Action<bool> affecter)
    {
        if (section[cle] is not string brut)
            return;
        if (bool.TryParse(brut, out var valeur))
            affecter(valeur);
        else
            options.ErreursLecture.Add($"{Section}:{cle} doit valoir true ou false.");
    }

    private static void Entier(IConfiguration section, string cle, ClosingOptions options, Action<int> affecter)
    {
        if (section[cle] is not string brut)
            return;
        if (int.TryParse(brut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valeur))
            affecter(valeur);
        else
            options.ErreursLecture.Add($"{Section}:{cle} doit être un nombre entier.");
    }

    /// <summary>Valeur d'énumération donnée par son NOM (un nombre est refusé).</summary>
    private static bool EnumNomme<TEnum>(string? brut, out TEnum valeur) where TEnum : struct, Enum
    {
        valeur = default;
        return !string.IsNullOrWhiteSpace(brut)
               && !char.IsDigit(brut.Trim()[0]) && brut.Trim()[0] != '-'
               && Enum.TryParse(brut.Trim(), ignoreCase: true, out valeur)
               && Enum.IsDefined(valeur);
    }
}
