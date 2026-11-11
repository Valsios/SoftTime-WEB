namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Modification de droits faite par le compte connecté : cible, action (<see cref="RightsChangeActions"/>) et valeurs
/// avant / après. L'opérateur est lu par le journal dans le contexte de la requête.
/// </summary>
public sealed record RightsChangeEntry(
    string TargetType,
    decimal TargetId,
    string? TargetName,
    string Action,
    IEnumerable<string>? Before,
    IEnumerable<string>? After);
