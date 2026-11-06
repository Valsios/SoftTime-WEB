namespace SoftTime.Application.DTOs;

/// <summary>État du moteur pour la société active.</summary>
public sealed record MlStatusDto(
    bool Enabled,
    string Etat,
    string? Message,
    DateTime? DerniereGeneration,
    // Plafond hebdomadaire des HS exonérées (au-delà, imposables) : seuil de pilotage.
    decimal PlafondHsExonere,
    IReadOnlyList<MlModelDto> Modeles);
