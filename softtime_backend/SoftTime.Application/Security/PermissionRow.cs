namespace SoftTime.Application.Security;

/// <summary>Ligne de permission lue en base. Rôle : IsGranted = droit accordé. Utilisateur : true = ajout, false = retrait.</summary>
public sealed record PermissionRow(string Code, bool IsGranted = true);
