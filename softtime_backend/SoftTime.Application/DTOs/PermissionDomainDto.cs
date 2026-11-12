namespace SoftTime.Application.DTOs;

/// <summary>
/// Un domaine du catalogue des permissions : clé (préfixe des codes : PARAMS, PROC, OTHER, TRACE, REPORTS, SELF), porte
/// qui l'ouvre (0 = aucune, pour SELF) et codes connus dans l'ordre d'affichage.
/// </summary>
public sealed record PermissionDomainDto(string Key, int Gate, IReadOnlyList<string> Codes);
