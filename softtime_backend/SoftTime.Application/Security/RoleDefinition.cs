namespace SoftTime.Application.Security;

/// <summary>Rôle tel que lu en base : lignes détaillées (T_ROLE_PERMISSION) et portes (T_DROIT_ROLE).</summary>
public sealed record RoleDefinition(
    decimal? Id,
    bool Exists,
    IReadOnlyCollection<PermissionRow> Rows,
    IReadOnlyCollection<int> Gates);
