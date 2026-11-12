namespace SoftTime.Application.DTOs;

/// <summary>
/// Paramètres de requête de l'analyse de clôture. <see cref="Gravites"/> liste des niveaux séparés par des virgules
/// (Information, Mineure, Importante, Critique) ; <see cref="Types"/> liste des types d'anomalie séparés par des
/// virgules ; <see cref="Matricule"/> restreint la liste à un salarié ; <see cref="Tri"/> n'accepte que « gravite ».
/// </summary>
public sealed record ClosingAnalysisQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Departement = null,
    bool? IncludeComparison = null,
    bool? IncludeInformation = null,
    int? Page = null,
    int? PageSize = null,
    bool? ReuseCache = null,
    string? Gravites = null,
    string? Tri = null,
    string? Types = null,
    string? Matricule = null);
