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
