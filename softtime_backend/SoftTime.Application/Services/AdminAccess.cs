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

/// <summary>Changement de rôle refusé tant que l'effacement des ajustements personnels n'est pas confirmé (HTTP 409, corps = aperçu).</summary>
public class RoleChangeConfirmationRequiredException : ConflictException
{
    public RoleChangeConfirmationRequiredException(SoftTime.Application.DTOs.RoleChangePreviewDto preview)
        : base("Le changement de rôle efface des droits personnalisés : confirmation requise.")
        => Preview = preview;

    public SoftTime.Application.DTOs.RoleChangePreviewDto Preview { get; }

    /// <summary>Le client lit l'aperçu dans le corps du 409, demande confirmation puis renvoie confirmClearAdjustments = true.</summary>
    public override object? Details => Preview;
}
