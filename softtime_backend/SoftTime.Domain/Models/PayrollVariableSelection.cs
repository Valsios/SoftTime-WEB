namespace SoftTime.Domain.Models;

/// <summary>
/// Variables envoyées à la paie pour une base SAGE. Le calcul ne dépend jamais de ce choix : les neuf variables
/// restent calculées et affichées, seul l'envoi vers la paie est filtré. Les valeurs par défaut reproduisent le
/// comportement historique (les sept variables d'heures supplémentaires actives, Retard et Absence inactives).
/// Le Domain ne lit aucune configuration : l'appelant fournit la sélection, comme <see cref="ClosingRulesOptions"/>.
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
    public static IReadOnlyList<string> HeuresSup { get; } = [Exo130, Exo150, I130, I150, Ferie, Dimanche, Nuit];

    /// <summary>Les neuf variables affichées par la page Heures supplémentaires.</summary>
    public static IReadOnlyList<string> Toutes { get; } = [.. HeuresSup, Retard, Absence];

    /// <summary>Variables dont l'envoi réel n'existe pas encore (lot C) : elles ne sont jamais écrites.</summary>
    public static IReadOnlyList<string> SansEnvoi { get; } = [Retard, Absence];

    /// <summary>Comportement historique : les sept variables d'heures supplémentaires.</summary>
    public static PayrollVariableSelection Default { get; } = new();

    /// <summary>Noms canoniques des variables actives.</summary>
    public IReadOnlySet<string> Actives { get; init; } = new HashSet<string>(HeuresSup, StringComparer.Ordinal);

    public bool EstActive(string variable) => Actives.Contains(variable);

    /// <summary>Variables d'heures supplémentaires actives, dans l'ordre historique d'écriture.</summary>
    public IReadOnlyList<string> HeuresSupActives => HeuresSup.Where(EstActive).ToList();

    /// <summary>Sélection construite à partir d'une liste de variables actives (casse indifférente).</summary>
    public static PayrollVariableSelection Avec(IEnumerable<string> actives) =>
        new() { Actives = actives.Select(Normaliser).ToHashSet(StringComparer.Ordinal) };

    /// <summary>Nom canonique d'une variable ; une variable inconnue est refusée.</summary>
    public static string Normaliser(string? variable) =>
        Toutes.FirstOrDefault(v => string.Equals(v, variable?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"Variable de paie inconnue : '{variable}'.");
}
