namespace SoftTime.Application.DTOs;

/// <summary>Filtres facultatifs de <c>GET api/journal</c> ; les dates sont en UTC.</summary>
public sealed record JournalQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? User = null,
    string? Action = null,
    string? Table = null,
    string? Key = null,
    Guid? Correlation = null,
    string? SageDatabase = null,
    int? Page = null,
    int? PageSize = null);
