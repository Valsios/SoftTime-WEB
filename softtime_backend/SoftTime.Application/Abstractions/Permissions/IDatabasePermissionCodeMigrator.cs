namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Fait suivre les codes de permission d'une base (<c>SAGE:&lt;nom&gt;:READ|UPDATE|DELETE|CREATE</c>) quand la base est
/// renommée ou supprimée, sur les rôles et sur les comptes. N'enregistre rien : l'appelant fait un seul
/// SaveChangesAsync avec sa propre modification, pour que tout soit appliqué ou rien.
/// </summary>
public interface IDatabasePermissionCodeMigrator
{
    Task RenameAsync(string prefix, string oldName, string newName, CancellationToken ct = default);

    Task RemoveAsync(string prefix, string name, CancellationToken ct = default);
}
