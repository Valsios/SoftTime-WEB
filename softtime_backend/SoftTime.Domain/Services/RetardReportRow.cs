namespace SoftTime.Domain.Services;

/// <summary>
/// Ligne de l'état des retards : un salarié, un jour, l'heure d'entrée retenue, l'heure prévue
/// au planning (avec la tolérance de sa catégorie) et le retard calculé en minutes.
/// </summary>
public sealed record RetardReportRow(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime Date,
    TimeSpan? HeureEntree,
    TimeSpan? HeurePrevue,
    int ToleranceMinutes,
    int MinutesRetard,
    bool RetardSaisi
);
