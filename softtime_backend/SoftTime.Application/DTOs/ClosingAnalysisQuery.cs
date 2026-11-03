namespace SoftTime.Application.DTOs;

/// <summary>Paramètres de l'écran Clôture (chaîne de requête) avant normalisation en <see cref="ClosingAnalysisRequest"/>.</summary>
public sealed record ClosingAnalysisQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Departement = null,
    bool? IncludeComparison = null,
    bool? IncludeInformation = null,
    int? Page = null,
    int? PageSize = null);
