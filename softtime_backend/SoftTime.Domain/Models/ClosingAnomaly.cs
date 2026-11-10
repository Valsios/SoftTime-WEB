namespace SoftTime.Domain.Models;

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
    decimal? Heures = null,
    bool? PointageExiste = null
);
