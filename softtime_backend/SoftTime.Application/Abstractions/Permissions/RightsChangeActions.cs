namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>Types de cible et actions du journal des droits (colonnes TYPE_CIBLE et ACTION de T_AUDIT_DROIT).</summary>
public static class RightsChangeActions
{
    public const string RoleTarget = "ROLE";
    public const string UserTarget = "USER";

    public const string CreateRole = "CREATE_ROLE";
    public const string RenameRole = "RENAME_ROLE";
    public const string DeleteRole = "DELETE_ROLE";
    public const string UpdateRoleGates = "UPDATE_ROLE_GATES";
    public const string UpdateDatabaseAccess = "UPDATE_DB_ACCESS";
}
