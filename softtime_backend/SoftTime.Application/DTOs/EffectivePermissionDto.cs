namespace SoftTime.Application.DTOs;

/// <summary>
/// Un droit d'un compte avec sa source : <see cref="PermissionSources"/>.Role (hérité du rôle), Added (ajouté au compte)
/// ou Removed (retiré au compte). IsEffective : le serveur l'accorde réellement. IsLegacy : ancien code de base sans
/// action (« SAGE:X »), visible pour être retiré en connaissance de cause (jamais supprimé automatiquement).
/// </summary>
public sealed record EffectivePermissionDto(string Code, string Source, bool IsEffective, bool IsLegacy);
