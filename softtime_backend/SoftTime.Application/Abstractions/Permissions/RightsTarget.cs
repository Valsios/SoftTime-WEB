namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>Cible d'une modification de droits : « ROLE » ou « USER », identifiant et nom affiché.</summary>
public sealed record RightsTarget(string Type, decimal Id, string? Name);
