using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>Une variable telle que l'utilisateur veut l'enregistrer pour une base.</summary>
/// <param name="Variable">Nom canonique (voir <see cref="PayrollVariableSelection.Toutes"/>).</param>
/// <param name="Actif">Envoyée à la paie.</param>
/// <param name="CodePaie">Base SAGE : code de paie propre à la base (variables sans code commun).</param>
/// <param name="Unite">Variables sans code commun : unité choisie (<see cref="PayrollUnits"/>).</param>
/// <param name="ColonneCible">Autre base : colonne de la table d'écriture (variables sans code commun).</param>
public sealed record PayrollVariableSetting(string Variable, bool Actif, string? CodePaie, string? Unite, string? ColonneCible);

/// <summary>Règles pures de la configuration des variables envoyées à la paie.</summary>
public static class PayrollVariableRules
{
    public const string UniteHeures = PayrollUnits.Heures;
    public const string UniteJours = PayrollUnits.Jours;

    /// <summary>
    /// Erreurs de validation d'une configuration complète (neuf variables, une fois chacune). Règle unique : une
    /// variable cochée doit avoir une destination valide. Une variable du catalogue commun des codes a toujours la
    /// sienne ; les autres exigent un code de paie (base SAGE) ou une colonne cible (autre base), et une unité admise.
    /// </summary>
    public static IReadOnlyList<string> Valider(IReadOnlyList<PayrollVariableSetting> lignes, bool baseSage)
    {
        var erreurs = new List<string>();
        var noms = new List<string>();
        foreach (var ligne in lignes)
        {
            var definition = PayrollVariableCatalog.Trouver(ligne.Variable);
            if (definition is null)
            {
                erreurs.Add($"Variable de paie inconnue : « {ligne.Variable} ».");
                continue;
            }
            noms.Add(definition.Id);
            if (!definition.CodeCommun)
                erreurs.AddRange(ValiderDestination(definition, ligne, baseSage));
        }
        foreach (var doublon in noms.GroupBy(n => n).Where(g => g.Count() > 1))
            erreurs.Add($"La variable {Libelle(doublon.Key)} apparaît plusieurs fois.");
        foreach (var manquante in PayrollVariableSelection.Toutes.Except(noms))
            erreurs.Add($"La variable {Libelle(manquante)} est absente de la configuration.");
        return erreurs;
    }

    /// <summary>Destination et unité d'une variable dont le code est propre à la base.</summary>
    private static IEnumerable<string> ValiderDestination(PayrollVariableDefinition definition, PayrollVariableSetting ligne, bool baseSage)
    {
        var nom = definition.Libelle;
        var unite = ligne.Unite?.Trim();
        var unites = string.Join(" ou ", definition.Unites.Select(u => $"« {PayrollUnits.Libelle(u)} »"));
        if (!string.IsNullOrEmpty(unite) && !definition.Unites.Contains(unite))
            yield return $"L'unité de la variable {nom} doit être {unites}.";
        if (!ligne.Actif)
            yield break;
        if (baseSage && string.IsNullOrWhiteSpace(ligne.CodePaie))
            yield return $"La variable {nom} ne peut pas être cochée tant que son code de paie n'est pas renseigné.";
        if (!baseSage && string.IsNullOrWhiteSpace(ligne.ColonneCible))
            yield return $"La variable {nom} ne peut pas être cochée tant que sa colonne cible n'est pas renseignée.";
        if (string.IsNullOrEmpty(unite))
            yield return $"La variable {nom} ne peut pas être cochée sans unité ({string.Join(" ou ", definition.Unites.Select(PayrollUnits.Libelle))}).";
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
    public static string Libelle(string variable) => PayrollVariableCatalog.Trouver(variable)?.Libelle ?? variable;
}
