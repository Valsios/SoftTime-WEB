namespace SoftTime.Domain.Models;

/// <summary>
/// Types d'anomalie contrôlés avant la clôture de paie. Les heures supplémentaires inhabituelles
/// et le croisement congé SAGE / pointage relèvent d'une phase ultérieure.
/// </summary>
public enum ClosingAnomalyType
{
    PointageIncomplet,
    PointageManquant,
    PlanningSansPointage,
    PointageSansPlanning,
    DoublonExact,
    QuasiDoublon,
    FerieNonApplique
}

/// <summary>Gravité d'une anomalie de clôture : la valeur numérique est le poids par défaut du score.</summary>
public enum ClosingSeverity
{
    Mineure = 1,
    Importante = 3,
    Critique = 5
}

/// <summary>
/// Anomalie de clôture pour un salarié et un jour. <see cref="Matricule"/> est la clé canonique
/// (<see cref="Services.MatriculeIdentity.Cle"/>) ; aucun nom ni prénom n'est porté.
/// </summary>
public sealed record ClosingAnomaly(
    ClosingAnomalyType Type,
    ClosingSeverity Severity,
    string Matricule,
    DateTime Date,
    string Detail,
    decimal? Heures = null
);

/// <summary>
/// Paramètres des règles de clôture. Les valeurs par défaut sont fixées dans le code : le Domain
/// ne lit aucune configuration, c'est à l'appelant de fournir d'autres valeurs.
/// </summary>
public sealed record ClosingRulesOptions
{
    public const int DefaultDuplicateToleranceSeconds = 120;

    public static ClosingRulesOptions Default { get; } = new();

    /// <summary>Deux pointages séparés de strictement moins de ce délai sont des quasi-doublons.</summary>
    public int DuplicateToleranceSeconds { get; init; } = DefaultDuplicateToleranceSeconds;

    public int PoidsMineure { get; init; } = (int)ClosingSeverity.Mineure;

    public int PoidsImportante { get; init; } = (int)ClosingSeverity.Importante;

    public int PoidsCritique { get; init; } = (int)ClosingSeverity.Critique;

    /// <summary>Poids d'une gravité ; un poids négatif est ramené à 0.</summary>
    public int Poids(ClosingSeverity gravite) => Math.Max(0, gravite switch
    {
        ClosingSeverity.Mineure => PoidsMineure,
        ClosingSeverity.Importante => PoidsImportante,
        ClosingSeverity.Critique => PoidsCritique,
        _ => 0
    });

    public int PoidsMaximum => Math.Max(Poids(ClosingSeverity.Mineure),
        Math.Max(Poids(ClosingSeverity.Importante), Poids(ClosingSeverity.Critique)));
}
