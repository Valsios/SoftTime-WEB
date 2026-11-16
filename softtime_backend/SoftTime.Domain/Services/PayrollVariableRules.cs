using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>Une variable telle que l'utilisateur veut l'enregistrer pour une base.</summary>
/// <param name="Variable">Nom canonique (voir <see cref="PayrollVariableSelection.Toutes"/>).</param>
/// <param name="Actif">Envoyée à la paie.</param>
/// <param name="CodePaie">Base SAGE : code constante (Retard, Absence seulement).</param>
/// <param name="Unite">Retard, Absence : « H » (heures) ou « J » (jours).</param>
/// <param name="ColonneCible">Autre base : colonne de la table d'écriture (Retard, Absence seulement).</param>
public sealed record PayrollVariableSetting(string Variable, bool Actif, string? CodePaie, string? Unite, string? ColonneCible);

/// <summary>Règles pures de la configuration des variables envoyées à la paie.</summary>
public static class PayrollVariableRules
{
    public const string UniteHeures = "H";
    public const string UniteJours = "J";

    /// <summary>
    /// Erreurs de validation d'une configuration complète (neuf variables, une fois chacune). Retard et Absence ne
    /// peuvent être actives que si leur code de paie (base SAGE) ou leur colonne cible (autre base) est renseigné.
    /// </summary>
    public static IReadOnlyList<string> Valider(IReadOnlyList<PayrollVariableSetting> lignes, bool baseSage)
    {
        var erreurs = new List<string>();
        var noms = new List<string>();
        foreach (var ligne in lignes)
        {
            var nom = PayrollVariableSelection.Toutes.FirstOrDefault(v => string.Equals(v, ligne.Variable?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (nom is null)
            {
                erreurs.Add($"Variable de paie inconnue : « {ligne.Variable} ».");
                continue;
            }
            noms.Add(nom);
            if (PayrollVariableSelection.SansEnvoi.Contains(nom))
                erreurs.AddRange(ValiderSansEnvoi(nom, ligne, baseSage));
        }
        foreach (var doublon in noms.GroupBy(n => n).Where(g => g.Count() > 1))
            erreurs.Add($"La variable {Libelle(doublon.Key)} apparaît plusieurs fois.");
        foreach (var manquante in PayrollVariableSelection.Toutes.Except(noms))
            erreurs.Add($"La variable {Libelle(manquante)} est absente de la configuration.");
        return erreurs;
    }

    private static IEnumerable<string> ValiderSansEnvoi(string nom, PayrollVariableSetting ligne, bool baseSage)
    {
        var unite = ligne.Unite?.Trim();
        if (!string.IsNullOrEmpty(unite) && unite is not (UniteHeures or UniteJours))
            yield return $"L'unité de la variable {Libelle(nom)} doit être « heures » ou « jours ».";
        if (!ligne.Actif)
            yield break;
        if (baseSage && string.IsNullOrWhiteSpace(ligne.CodePaie))
            yield return $"La variable {Libelle(nom)} ne peut pas être cochée tant que son code de paie n'est pas renseigné.";
        if (!baseSage && string.IsNullOrWhiteSpace(ligne.ColonneCible))
            yield return $"La variable {Libelle(nom)} ne peut pas être cochée tant que sa colonne cible n'est pas renseignée.";
        if (string.IsNullOrEmpty(unite))
            yield return $"La variable {Libelle(nom)} ne peut pas être cochée sans unité (heures ou jours).";
    }

    /// <summary>
    /// Vrai si une période déjà envoyée en paie recouvre le mois civil de <paramref name="aujourdHui"/>
    /// (du 1er au dernier jour inclus) : la configuration ne peut alors plus être modifiée.
    /// </summary>
    public static bool MoisEnCoursEnvoye(IEnumerable<(DateTime Debut, DateTime Fin)> periodesEnvoyees, DateTime aujourdHui)
    {
        var premier = new DateTime(aujourdHui.Year, aujourdHui.Month, 1);
        var dernier = premier.AddMonths(1).AddDays(-1);
        return periodesEnvoyees.Any(p => p.Debut.Date <= dernier && p.Fin.Date >= premier);
    }

    /// <summary>
    /// Anomalies du Contrôle avant paie qui ne portent que sur des variables non envoyées : « férié non appliqué »
    /// (Férié), « heures supplémentaires atypiques » (les quatre variables EXO/IMPO). Vide avec la sélection par défaut.
    /// </summary>
    public static IReadOnlySet<ClosingAnomalyType> TypesSansObjet(PayrollVariableSelection selection)
    {
        var types = new HashSet<ClosingAnomalyType>();
        if (!selection.EstActive(PayrollVariableSelection.Ferie))
            types.Add(ClosingAnomalyType.FerieNonApplique);
        if (new[] { PayrollVariableSelection.Exo130, PayrollVariableSelection.Exo150, PayrollVariableSelection.I130, PayrollVariableSelection.I150 }
            .All(v => !selection.EstActive(v)))
            types.Add(ClosingAnomalyType.HeuresSupAtypiques);
        return types;
    }

    /// <summary>Libellé affiché d'une variable (page Heures supplémentaires).</summary>
    public static string Libelle(string variable) => variable switch
    {
        PayrollVariableSelection.Exo130 => "EXO 130",
        PayrollVariableSelection.Exo150 => "EXO 150",
        PayrollVariableSelection.I130 => "IMPO 130",
        PayrollVariableSelection.I150 => "IMPO 150",
        PayrollVariableSelection.Ferie => "Férié",
        PayrollVariableSelection.Dimanche => "Dimanche",
        PayrollVariableSelection.Nuit => "Nuit",
        PayrollVariableSelection.Retard => "Retard",
        PayrollVariableSelection.Absence => "Absence",
        _ => variable
    };
}
