namespace SoftTime.Domain.Models;

/// <summary>Unités possibles d'une variable de paie (codes stockés dans T_PAIE_VARIABLE.UNITE).</summary>
public static class PayrollUnits
{
    public const string Heures = "H";
    public const string Jours = "J";

    /// <summary>Libellé affiché d'une unité.</summary>
    public static string Libelle(string unite) => unite switch
    {
        Heures => "heures",
        Jours => "jours",
        _ => unite
    };
}

/// <summary>
/// Valeurs d'une ligne de résultat de la période (une ligne EXO/IMPO par salarié), une par variable de paie. Une
/// valeur absente vaut zéro à l'envoi.
/// </summary>
public sealed record PayrollRowValues(
    decimal? Exo130,
    decimal? Exo150,
    decimal? I130,
    decimal? I150,
    decimal? Ferie,
    decimal? Nuit,
    decimal? Dimanche,
    decimal? Retard,
    decimal? Absence);

/// <summary>
/// Contrat d'une variable de paie : ce qu'elle est (identifiant, libellé, unités), d'où vient sa valeur et comment sa
/// destination est désignée. Rien n'y est propre à un logiciel de paie : la destination est un code (désigné dans le
/// catalogue commun des codes, ou propre à la base) ou une colonne, résolue par l'écriture de paie.
/// </summary>
/// <param name="Id">Identifiant canonique (clé de configuration et de journal).</param>
/// <param name="Libelle">Libellé affiché.</param>
/// <param name="HeureSupplementaire">Fait partie des sept variables d'heures supplémentaires.</param>
/// <param name="CleCodeCommun">Clé de la variable dans le catalogue commun des codes de paie ; <c>null</c> : le code est
/// choisi dans la configuration de chaque base.</param>
/// <param name="Unites">Unités admises (<see cref="PayrollUnits"/>), la première étant celle par défaut.</param>
/// <param name="Envoyable">L'écriture de la variable vers la paie existe.</param>
/// <param name="Valeur">Valeur de la variable dans une ligne de résultat.</param>
public sealed record PayrollVariableDefinition(
    string Id,
    string Libelle,
    bool HeureSupplementaire,
    string? CleCodeCommun,
    IReadOnlyList<string> Unites,
    bool Envoyable,
    Func<PayrollRowValues, decimal?> Valeur)
{
    /// <summary>Le code de destination est commun à toutes les bases (catalogue des codes).</summary>
    public bool CodeCommun => CleCodeCommun is not null;

    /// <summary>Valeur envoyée : une valeur absente vaut zéro.</summary>
    public decimal ValeurEnvoyee(PayrollRowValues ligne) => Valeur(ligne) ?? 0m;
}

/// <summary>
/// Liste unique des variables de paie, dans l'ordre historique d'écriture (EXO 130, EXO 150, IMPO 130, IMPO 150, Férié,
/// Dimanche, Nuit), suivies de Retard et d'Absence. Toutes les listes de variables du code en dérivent.
/// </summary>
public static class PayrollVariableCatalog
{
    private static readonly IReadOnlyList<string> HeuresSeules = [PayrollUnits.Heures];
    private static readonly IReadOnlyList<string> HeuresOuJours = [PayrollUnits.Heures, PayrollUnits.Jours];

    public static IReadOnlyList<PayrollVariableDefinition> Toutes { get; } =
    [
        new(PayrollVariableSelection.Exo130, "EXO 130", true, Services.OvertimeCodeKeys.Exo130, HeuresSeules, true, l => l.Exo130),
        new(PayrollVariableSelection.Exo150, "EXO 150", true, Services.OvertimeCodeKeys.Exo150, HeuresSeules, true, l => l.Exo150),
        new(PayrollVariableSelection.I130, "IMPO 130", true, Services.OvertimeCodeKeys.I130, HeuresSeules, true, l => l.I130),
        new(PayrollVariableSelection.I150, "IMPO 150", true, Services.OvertimeCodeKeys.I150, HeuresSeules, true, l => l.I150),
        new(PayrollVariableSelection.Ferie, "Férié", true, Services.OvertimeCodeKeys.Ferie, HeuresSeules, true, l => l.Ferie),
        new(PayrollVariableSelection.Dimanche, "Dimanche", true, Services.OvertimeCodeKeys.Dim, HeuresSeules, true, l => l.Dimanche),
        new(PayrollVariableSelection.Nuit, "Nuit", true, Services.OvertimeCodeKeys.Nuit, HeuresSeules, true, l => l.Nuit),
        // Retard (lot C2) : envoyé en heures, code ou colonne propre à chaque base.
        new(PayrollVariableSelection.Retard, "Retard", false, null, HeuresSeules, true, l => l.Retard),
        // Absence : jamais envoyée tant que la définition des absences non justifiées n'est pas arrêtée (lot C3).
        new(PayrollVariableSelection.Absence, "Absence", false, null, HeuresOuJours, false, l => l.Absence),
    ];

    /// <summary>Définition d'une variable ; une variable inconnue est refusée.</summary>
    public static PayrollVariableDefinition Definition(string? id) =>
        Toutes.FirstOrDefault(d => string.Equals(d.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"Variable de paie inconnue : '{id}'.");

    /// <summary>Définition d'une variable, ou <c>null</c> si l'identifiant est inconnu.</summary>
    public static PayrollVariableDefinition? Trouver(string? id) =>
        Toutes.FirstOrDefault(d => string.Equals(d.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
}
