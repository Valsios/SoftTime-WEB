namespace SoftTime.Application.DTOs;

/// <summary>Filtre et page du journal d'audit (paramètres de requête).</summary>
public sealed record AuditEventsQuery(DateTime? From = null, DateTime? To = null, string? Table = null, string? Action = null, string? Search = null, int? Page = null, int? PageSize = null);
