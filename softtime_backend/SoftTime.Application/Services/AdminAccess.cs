using SoftTime.Application.Abstractions;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>Opérations réservées aux administrateurs (rôle Administrateur, IDROLE 1).</summary>
public static class AdminAccess
{
    public const string ReservedMessage = "Cette opération est réservée aux administrateurs.";

    public static bool IsAdmin(ICurrentUser user) => user.RoleId == AdminRules.AdminRoleId;

    public static void Require(ICurrentUser user)
    {
        if (!IsAdmin(user))
            throw new ForbiddenException(ReservedMessage);
    }
}
