namespace SoftTime.Application.DTOs;

/// <summary>Checklist d'avancement de la paie d'une période : séparée de l'analyse, n'entre ni dans le score ni dans le
/// statut.</summary>
public sealed record ClosingChecklistDto(
    DateTime From,
    DateTime To,
    string? Departement,
    IReadOnlyList<ClosingChecklistLineDto> Lignes);
