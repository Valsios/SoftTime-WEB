namespace SoftTime.Application.Security;

/// <summary>
/// Lectures sensibles de la section Paramètres : permission fine requise (l'une ou l'autre quand plusieurs écrans
/// partagent la route), et message renvoyé quand elle manque.
/// </summary>
public static class ReadPermissions
{
    public const string Denied = "Vous n'avez pas la permission requise pour cette consultation.";

    public static readonly string[] Users = { "PARAMS:USERS" };
    public static readonly string[] UserList = { "PARAMS:USERS", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" };
    public static readonly string[] Roles = { "PARAMS:ROLES" };
    public static readonly string[] RoleList = { "PARAMS:USERS", "PARAMS:PRIVILEGES" };
    public static readonly string[] Privileges = { "PARAMS:PRIVILEGES" };
    public static readonly string[] PrivilegeList = { "PARAMS:PRIVILEGES", "PARAMS:ROLES" };
    public static readonly string[] DbAccess = { "PARAMS:DB_ACCESS" };
}
