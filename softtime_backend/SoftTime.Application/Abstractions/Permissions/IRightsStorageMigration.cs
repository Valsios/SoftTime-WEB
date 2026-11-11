namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Passage unique au stockage des droits en écarts : rôles sans ligne convertis en droits détaillés (d'après leurs
/// portes), listes complètes des comptes converties en ajouts et retraits. Rejouable : ne fait rien une fois inscrite.
/// </summary>
public interface IRightsStorageMigration
{
    Task<RightsStorageMigrationResult> RunAsync(CancellationToken cancellationToken = default);
}
