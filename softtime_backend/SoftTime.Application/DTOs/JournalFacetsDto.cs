namespace SoftTime.Application.DTOs;

/// <summary>Valeurs distinctes de la période, plafonnées, pour alimenter les listes de filtres.</summary>
public sealed record JournalFacetsDto(
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Users);
