namespace SoftTime.Application.DTOs;

/// <summary>Paramètres de requête de la checklist d'avancement de la paie.</summary>
public sealed record ClosingChecklistQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Departement = null,
    bool? ReuseCache = null);
