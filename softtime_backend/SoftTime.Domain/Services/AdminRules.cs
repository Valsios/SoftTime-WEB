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
    /// Vérifie si roleId == AdminRoleId (1) OU si le libellé contient "ADMIN" (insensible à la casse).
    /// </summary>
    public static bool IsAdmin(decimal roleId, string? roleNom = null)
    {
        if (roleId == AdminRoleId)
            return true;

        if (!string.IsNullOrWhiteSpace(roleNom) && roleNom.Contains("ADMIN", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Surcharge pour une entité rôle.
    /// </summary>
    public static bool IsAdmin(T_ROLE? role)
        => role != null && IsAdmin(role.IDROLE, role.NOMROLE);

    /// <summary>
    /// Détermine si un identifiant de rôle correspond au super-administrateur irrévocable (RoleId == 1).
    /// Contrairement à IsAdmin qui reconnaît également les rôles personnalisés dont le nom contient "ADMIN",
    /// seul AdminRoleId détient les super-droits serveur natifs (SecurityContext.cs:24).
    /// </summary>
    public static bool IsSuperAdmin(decimal roleId) => roleId == AdminRoleId;
}
