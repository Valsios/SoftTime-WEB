using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles métier centralisées relatives aux privilèges d'administration.
/// </summary>
public static class AdminRules
{
    /// <summary>
    /// Identifiant immuable du rôle Administrateur système.
    /// </summary>
    public const decimal AdminRoleId = 1;

    /// <summary>
    /// Détermine si un rôle confère des droits d'administration applicatifs.
    /// Seul le rôle 1 est administrateur : le libellé du rôle n'est plus pris en compte (roleNom est ignoré).
    /// </summary>
    public static bool IsAdmin(decimal roleId, string? roleNom = null)
        => roleId == AdminRoleId;

    /// <summary>
    /// Surcharge pour une entité rôle.
    /// </summary>
    public static bool IsAdmin(T_ROLE? role)
        => role != null && IsAdmin(role.IDROLE, role.NOMROLE);

    /// <summary>
    /// Détermine si un identifiant de rôle correspond au super-administrateur irrévocable (RoleId == 1).
    /// IsAdmin et IsSuperAdmin sont désormais équivalents : le nom du rôle ne confère aucun droit,
    /// seul AdminRoleId détient les super-droits serveur natifs (SecurityContext.cs:24).
    /// </summary>
    public static bool IsSuperAdmin(decimal roleId) => roleId == AdminRoleId;
}
