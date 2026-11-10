namespace SoftTime.Application.DTOs;

/// <summary>Anomalie exposée : matricule canonique uniquement, aucun nom ni prénom.</summary>
public sealed record ClosingAnomalyDto(
    string Type,
    string Gravite,
    int Poids,
    string Matricule,
    string? Departement,
    DateTime Date,
    string Detail,
    decimal? Heures,
    bool? PointageExiste = null);
