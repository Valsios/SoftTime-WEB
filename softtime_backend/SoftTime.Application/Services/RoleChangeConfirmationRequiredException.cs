namespace SoftTime.Application.Services;

/// <summary>Changement de rôle refusé tant que l'effacement des ajustements personnels n'est pas confirmé (HTTP 409, corps = aperçu).</summary>
public sealed class RoleChangeConfirmationRequiredException : ConflictException
{
    public RoleChangeConfirmationRequiredException(SoftTime.Application.DTOs.RoleChangePreviewDto preview)
        : base("Le changement de rôle efface des droits personnalisés : confirmation requise.")
        => Preview = preview;

    public SoftTime.Application.DTOs.RoleChangePreviewDto Preview { get; }
}
