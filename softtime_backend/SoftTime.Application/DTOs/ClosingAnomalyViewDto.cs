namespace SoftTime.Application.DTOs;

/// <summary>Anomalie présentée à l'écran : nom et prénom <c>null</c> quand l'affichage des noms est désactivé.</summary>
public sealed record ClosingAnomalyViewDto(
    string Type,
    string Gravite,
    int Poids,
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime Date,
    string Detail,
    decimal? Heures,
    bool? PointageExiste = null);
