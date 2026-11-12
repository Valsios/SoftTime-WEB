namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Reprise des données de la migration 006 : convertit les anciennes copies complètes des droits des comptes
/// (T_USER_PERMISSION.IS_DEVIATION = 0) en écarts par rapport au rôle, sans changer les droits effectifs.
/// Rejouable : ne touche que les lignes encore au format copie ; une seule sauvegarde.
/// </summary>
public interface IUserPermissionDeviationMigration
{
    /// <summary>Nombre de comptes convertis (0 si tout est déjà au format écart).</summary>
    Task<int> RunAsync(CancellationToken ct = default);
}
