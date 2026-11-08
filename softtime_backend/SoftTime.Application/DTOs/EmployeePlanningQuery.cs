namespace SoftTime.Application.DTOs;

/// <summary>Période, salarié éventuel, recherche et page du planning (paramètres de requête).</summary>
public sealed record EmployeePlanningQuery(DateTime From, DateTime To, int? CardId = null, int? Page = null, int? PageSize = null, string? Search = null);
