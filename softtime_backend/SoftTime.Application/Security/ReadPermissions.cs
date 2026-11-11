namespace SoftTime.Application.Security;

/// <summary>
/// Lectures sensibles de la section Paramètres : permission fine requise (l'une ou l'autre quand plusieurs écrans
/// partagent la route), et message renvoyé quand elle manque.
/// </summary>
public static class ReadPermissions
{
    public const string Denied = "Vous n'avez pas la permission requise pour cette consultation.";

    public static readonly string[] Users = { "users.read" };
    public static readonly string[] UserList = { "users.read", "privileges.read", "db-access.read" };
    public static readonly string[] Roles = { "roles.read" };
    public static readonly string[] RoleList = { "users.read", "privileges.read" };
    public static readonly string[] Privileges = { "privileges.read" };
    public static readonly string[] PrivilegeList = { "privileges.read", "roles.read" };
    public static readonly string[] DbAccess = { "db-access.read" };
}
