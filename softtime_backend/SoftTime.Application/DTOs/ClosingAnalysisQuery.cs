namespace SoftTime.Application.DTOs;

/// <summary>Paramètres de requête de l'analyse de clôture.</summary>
public sealed record ClosingAnalysisQuery(DateTime? From = null, DateTime? To = null, string? Departement = null, bool? IncludeComparison = null, bool? IncludeInformation = null, int? Page = null, int? PageSize = null, bool? ReuseCache = null);
