namespace SoftTime.Application.DTOs;

/// <summary>Ligne du journal central telle qu'elle sort de l'API : valeurs sensibles toujours masquées.</summary>
public sealed record JournalEntryDto(
    long Id,
    DateTime DateUtc,
    string Table,
    string? Key,
    string? Column,
    string Action,
    string? Before,
    string? After,
    string User,
    string? SageDatabase,
    string? PointeuseDatabase,
    Guid Correlation,
    string Source);

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

public sealed record JournalPageDto(IReadOnlyList<JournalEntryDto> Items, int Total, int Page, int PageSize);

/// <summary>Valeurs distinctes de la période, plafonnées, pour alimenter les listes de filtres.</summary>
public sealed record JournalFacetsDto(
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Users);
