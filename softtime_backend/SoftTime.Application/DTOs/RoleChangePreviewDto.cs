namespace SoftTime.Application.DTOs;

/// <summary>Aperçu d'un changement de rôle : ajustements personnels qui seraient effacés (« +CODE » ajouté, « -CODE » retiré).</summary>
public record RoleChangePreviewDto(IReadOnlyList<string> AdjustmentsToClear, int Count, string AccountLogin);
