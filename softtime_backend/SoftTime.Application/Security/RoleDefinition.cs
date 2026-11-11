namespace SoftTime.Application.Security;

/// <summary>
/// Rôle tel que lu en base : ses lignes détaillées (T_ROLE_PERMISSION). Les portes (T_DROIT_ROLE) ne sont plus lues :
/// elles se déduisent toujours des droits effectifs.
/// </summary>
public sealed record RoleDefinition(
    decimal? Id,
    bool Exists,
    IReadOnlyCollection<PermissionRow> Rows);
