using SoftTime.Application.Abstractions;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>Opération refusée à l'appelant ; traduite en HTTP 403 avec le message.</summary>
public class ForbiddenOperationException : Exception
{
    public ForbiddenOperationException(string message) : base(message) { }
}

/// <summary>Opérations réservées aux administrateurs (rôle Administrateur, IDROLE 1).</summary>
public static class AdminAccess
{
    public const string ReservedMessage = "Cette opération est réservée aux administrateurs.";

    public static bool IsAdmin(ICurrentUser user) => user.RoleId == AdminRules.AdminRoleId;

    public static void Require(ICurrentUser user)
    {
        if (!IsAdmin(user))
            throw new ForbiddenOperationException(ReservedMessage);
    }
}
