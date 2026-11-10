namespace SoftTime.Application.DTOs;

/// <summary>
/// Paramètres de requête de l'analyse de clôture. <see cref="Gravites"/> liste des niveaux séparés par des virgules
/// (Information, Mineure, Importante, Critique) ; <see cref="Tri"/> n'accepte que « gravite ».
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
    string? Tri = null);
