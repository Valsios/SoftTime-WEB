namespace SoftTime.Application.Security;

/// <summary>
/// Une catégorie du catalogue : ses écrans et la porte que ses permissions ouvrent (null : permission sans porte,
/// comme Mon espace, jamais accordée par défaut avec un droit).
/// </summary>
public sealed record PermissionCategoryDefinition(
    string Key,
    string Title,
    int? Gate,
    IReadOnlyList<PermissionItemDefinition> Items);
