using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Options « Database:AutoMigrate » et « Database:AutoMigrateLockTimeoutSeconds ».</summary>
public sealed record SchemaMigrationOptions
{
    public const int DefaultLockTimeoutSeconds = 60;

    /// <summary>Exécute les scripts versionnés au démarrage. Désactivé par défaut.</summary>
    public bool AutoMigrate { get; init; }

    /// <summary>Attente maximale du verrou applicatif (deux démarrages simultanés).</summary>
    public int LockTimeoutSeconds { get; init; } = DefaultLockTimeoutSeconds;

    public static SchemaMigrationOptions FromConfiguration(IConfiguration configuration)
    {
        var autoMigrate = bool.TryParse(configuration["Database:AutoMigrate"], out var enabled) && enabled;
        var timeout = int.TryParse(configuration["Database:AutoMigrateLockTimeoutSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? seconds
            : DefaultLockTimeoutSeconds;
        return new SchemaMigrationOptions { AutoMigrate = autoMigrate, LockTimeoutSeconds = timeout };
    }
}

/// <summary>Échec d'une migration. Le message est en français et ne contient ni secret ni chaîne de connexion.</summary>
public sealed class SchemaMigrationException : Exception
{
    public SchemaMigrationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Version déjà appliquée, lue dans dbo.T_SCHEMA_VERSION.</summary>
public sealed record AppliedMigration(int Version, string Name, string Checksum);

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

public interface ISchemaMigrationConnectionFactory
{
    Task<ISchemaMigrationConnection> OpenAsync(CancellationToken ct);
}

public sealed record SchemaMigrationResult(IReadOnlyList<string> Applied, IReadOnlyList<string> ChecksumMismatches);

/// <summary>
/// Applique, dans l'ordre numérique, les scripts versionnés pas encore enregistrés dans
/// dbo.T_SCHEMA_VERSION. Chaque script s'exécute dans sa propre transaction ; un verrou
/// applicatif sp_getapplock (propriétaire Session) sérialise deux démarrages simultanés.
/// Un script déjà appliqué dont la somme de contrôle a changé n'est jamais rejoué : il est signalé.
/// </summary>
public sealed class SchemaMigrator
{
    public const string LockResource = "SoftTime.SchemaMigration";

    private readonly ISchemaMigrationConnectionFactory _connections;
    private readonly IMigrationScriptSource _scripts;
    private readonly SchemaMigrationOptions _options;
    private readonly ILogger<SchemaMigrator> _logger;

    public SchemaMigrator(
        ISchemaMigrationConnectionFactory connections,
        IMigrationScriptSource scripts,
        SchemaMigrationOptions options,
        ILogger<SchemaMigrator>? logger = null)
    {
        _connections = connections;
        _scripts = scripts;
        _options = options;
        _logger = logger ?? NullLogger<SchemaMigrator>.Instance;
    }

    /// <summary>Ne fait rien (aucune connexion) si Database:AutoMigrate est faux.</summary>
    public async Task<SchemaMigrationResult?> RunIfEnabledAsync(CancellationToken ct = default)
    {
        if (!_options.AutoMigrate)
            return null;
        return await RunAsync(ct);
    }

    public async Task<SchemaMigrationResult> RunAsync(CancellationToken ct = default)
    {
        var scripts = MigrationScriptNames.Order(_scripts.Load());
        await using var connection = await _connections.OpenAsync(ct);

        if (!await connection.TryAcquireLockAsync(_options.LockTimeoutSeconds, ct))
            throw new SchemaMigrationException(
                $"Migration du schéma impossible : le verrou « {LockResource} » n'a pas été obtenu en {_options.LockTimeoutSeconds} s. "
                + "Une autre instance de l'API applique peut-être les migrations ; réessayez après son démarrage.");
        try
        {
            await connection.EnsureVersionTableAsync(ct);
            var applied = (await connection.GetAppliedAsync(ct)).ToDictionary(a => a.Version);
            var appliedNow = new List<string>();
            var mismatches = new List<string>();

            foreach (var script in scripts)
            {
                if (applied.TryGetValue(script.Version, out var previous))
                {
                    if (!string.Equals(previous.Checksum, script.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add(script.Name);
                        _logger.LogWarning(
                            "Le script de migration {Script} a été modifié après son application (somme de contrôle {Recorded} enregistrée, {Current} actuelle). Il n'est pas rejoué : ajoutez un nouveau script.",
                            script.Name, previous.Checksum, script.Checksum);
                    }
                    continue;
                }

                await ApplyAsync(connection, script, ct);
                appliedNow.Add(script.Name);
            }

            if (appliedNow.Count == 0)
                _logger.LogInformation("Schéma à jour : aucune migration à appliquer.");
            return new SchemaMigrationResult(appliedNow, mismatches);
        }
        finally
        {
            try
            {
                await connection.ReleaseLockAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // La fermeture de la connexion libère de toute façon un verrou de session.
                _logger.LogWarning(ex, "Libération du verrou de migration impossible.");
            }
        }
    }

    private async Task ApplyAsync(ISchemaMigrationConnection connection, MigrationScript script, CancellationToken ct)
    {
        var batches = SqlBatchSplitter.Split(script.Content);
        _logger.LogInformation("Application de la migration {Script} ({Count} lots).", script.Name, batches.Count);
        await connection.BeginTransactionAsync(ct);
        var index = 0;
        try
        {
            for (; index < batches.Count; index++)
                await connection.ExecuteBatchAsync(batches[index], ct);
            await connection.RecordAppliedAsync(script, AppliedBy(), ct);
            await connection.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try
            {
                await connection.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackError)
            {
                // XACT_ABORT peut avoir déjà annulé la transaction côté serveur.
                _logger.LogDebug(rollbackError, "Annulation de la transaction de {Script} déjà effectuée par le serveur.", script.Name);
            }
            var where = index < batches.Count ? $"lot {index + 1} sur {batches.Count}" : "enregistrement de la version";
            _logger.LogError("Échec de la migration {Script} ({Where}) : {Error}. Transaction annulée.", script.Name, where, ex.Message);
            throw new SchemaMigrationException(
                $"Échec de la migration {script.Name} ({where}) : {ex.Message} La transaction a été annulée, la base est inchangée pour ce script.", ex);
        }
        _logger.LogInformation("Migration {Script} appliquée.", script.Name);
    }

    private static string AppliedBy()
    {
        var value = $"{Environment.MachineName}\\{Environment.UserName}";
        return value.Length <= 256 ? value : value[..256];
    }
}
