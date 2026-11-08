namespace SoftTime.Application.DTOs;

/// <summary>Filtre et page des affectations salarié → catégorie (paramètres de requête).</summary>
public sealed record AffectationsQuery(int? CategoryId = null, string? Departement = null, string? Service = null, int? Page = null, int? PageSize = null, string? Search = null);
