namespace SoftTime.Domain.Models;

/// <summary>
/// Variables envoyées à la paie pour une base SAGE. Le calcul ne dépend jamais de ce choix : les neuf variables
/// restent calculées et affichées, seul l'envoi vers la paie est filtré. Les valeurs par défaut reproduisent le
/// comportement historique (les sept variables d'heures supplémentaires actives, Retard et Absence inactives).
/// Le Domain ne lit aucune configuration : l'appelant fournit la sélection, comme <see cref="ClosingRulesOptions"/>.
/// Les listes de variables dérivent de <see cref="PayrollVariableCatalog"/>.
/// </summary>
public sealed record PayrollVariableSelection
{
    public const string Exo130 = "Exo130";
    public const string Exo150 = "Exo150";
    public const string I130 = "I130";
    public const string I150 = "I150";
    public const string Ferie = "Ferie";
    public const string Dimanche = "Dimanche";
    public const string Nuit = "Nuit";
    public const string Retard = "Retard";
    public const string Absence = "Absence";

    /// <summary>Les sept variables d'heures supplémentaires, dans l'ordre historique d'écriture.</summary>
    public static IReadOnlyList<string> HeuresSup { get; } =
        PayrollVariableCatalog.Toutes.Where(d => d.HeureSupplementaire).Select(d => d.Id).ToList();

    /// <summary>Les neuf variables affichées par la page Heures supplémentaires.</summary>
    public static IReadOnlyList<string> Toutes { get; } = PayrollVariableCatalog.Toutes.Select(d => d.Id).ToList();

    /// <summary>Variables dont l'envoi réel n'existe pas encore : elles ne sont jamais écrites.</summary>
    public static IReadOnlyList<string> SansEnvoi { get; } =
        PayrollVariableCatalog.Toutes.Where(d => !d.Envoyable).Select(d => d.Id).ToList();

    /// <summary>Aucun code propre à la base (déclaré avant <see cref="Default"/> : ordre des initialiseurs statiques).</summary>
    private static readonly IReadOnlyDictionary<string, string> AucunCode = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Comportement historique : les sept variables d'heures supplémentaires.</summary>
    public static PayrollVariableSelection Default { get; } = new();

    /// <summary>
    /// Codes de paie choisis dans la configuration de la base, pour les variables qui ne figurent pas dans le catalogue
    /// commun des codes (variable → code). Vide par défaut.
    /// </summary>
    public IReadOnlyDictionary<string, string> CodesPaie { get; init; } = AucunCode;

    /// <summary>Code de paie propre à la base d'une variable, ou <c>null</c>.</summary>
    public string? CodeDe(string variable) =>
        CodesPaie.TryGetValue(variable, out var code) && !string.IsNullOrWhiteSpace(code) ? code.Trim() : null;

    /// <summary>Noms canoniques des variables actives.</summary>
    public IReadOnlySet<string> Actives { get; init; } = new HashSet<string>(HeuresSup, StringComparer.Ordinal);

    public bool EstActive(string variable) => Actives.Contains(variable);

    /// <summary>Variables d'heures supplémentaires actives, dans l'ordre historique d'écriture.</summary>
    public IReadOnlyList<string> HeuresSupActives => HeuresSup.Where(EstActive).ToList();

    /// <summary>
    /// Variables réellement écrites en paie : actives ET envoyables, dans l'ordre du catalogue (l'ordre historique pour
    /// les sept variables d'heures supplémentaires). Avec la sélection par défaut : les sept, comme avant.
    /// </summary>
    public IReadOnlyList<string> Envoyees =>
        PayrollVariableCatalog.Toutes.Where(d => d.Envoyable && EstActive(d.Id)).Select(d => d.Id).ToList();

    /// <summary>Sélection construite à partir d'une liste de variables actives (casse indifférente).</summary>
    public static PayrollVariableSelection Avec(IEnumerable<string> actives) =>
        new() { Actives = actives.Select(Normaliser).ToHashSet(StringComparer.Ordinal) };

    /// <summary>Nom canonique d'une variable ; une variable inconnue est refusée.</summary>
    public static string Normaliser(string? variable) => PayrollVariableCatalog.Definition(variable).Id;
}
