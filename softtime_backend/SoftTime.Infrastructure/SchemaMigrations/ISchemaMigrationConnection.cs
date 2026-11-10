namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Accès à la base pendant une migration (une connexion, abstraite pour les tests).</summary>
public interface ISchemaMigrationConnection : IAsyncDisposable
{
    /// <summary>sp_getapplock exclusif ; faux si le verrou n'a pas été obtenu dans le délai.</summary>
    Task<bool> TryAcquireLockAsync(int timeoutSeconds, CancellationToken ct);
    Task ReleaseLockAsync(CancellationToken ct);
    /// <summary>Crée dbo.T_SCHEMA_VERSION si elle n'existe pas.</summary>
    Task EnsureVersionTableAsync(CancellationToken ct);
    Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken ct);
    Task BeginTransactionAsync(CancellationToken ct);
    Task ExecuteBatchAsync(string sql, CancellationToken ct);
    Task RecordAppliedAsync(MigrationScript script, string appliedBy, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
