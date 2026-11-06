using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SoftTime.Infrastructure.SchemaMigrations;

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
        IOptions<SchemaMigrationOptions> options,
        ILogger<SchemaMigrator>? logger = null)
    {
        _connections = connections;
        _scripts = scripts;
        _options = options.Value;
        _logger = logger ?? NullLogger<SchemaMigrator>.Instance;
    }

    /// <summary>Ne fait rien (aucune connexion) si Database:AutoMigrate est faux.</summary>
    public async Task<SchemaMigrationResult?> RunIfEnabledAsync(CancellationToken ct = default)
    {
        if (!_options.AutoMigrate)
        {
            _logger.LogInformation("{Message}", DisabledMessage);
            return null;
        }
        var result = await RunAsync(ct);
        _logger.LogInformation("{Message}", Summarize(result));
        return result;
    }

    public const string DisabledMessage = "Migrations de schéma désactivées (Database:AutoMigrate = false).";

    /// <summary>Ligne de journal du démarrage : sans secret, sans chaîne de connexion.</summary>
    public static string Summarize(SchemaMigrationResult result)
        => $"Migrations de schéma : {result.Applied.Count} script(s) appliqué(s), {result.AlreadyUpToDate} déjà à jour.";

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
            return await ApplyPendingAsync(connection, scripts, ct);
        }
        finally
        {
            await ReleaseLockAsync(connection);
        }
    }

    /// <summary>
    /// Applique les scripts absents de la table des versions. Un script déjà appliqué mais modifié
    /// depuis n'est pas rejoué : il est seulement signalé.
    /// </summary>
    private async Task<SchemaMigrationResult> ApplyPendingAsync(
        ISchemaMigrationConnection connection, IReadOnlyList<MigrationScript> scripts, CancellationToken ct)
    {
        await connection.EnsureVersionTableAsync(ct);
        var applied = (await connection.GetAppliedAsync(ct)).ToDictionary(a => a.Version);
        var appliedNow = new List<string>();
        var mismatches = new List<string>();
        foreach (var script in scripts)
        {
            if (!applied.TryGetValue(script.Version, out var previous))
            {
                await ApplyAsync(connection, script, ct);
                appliedNow.Add(script.Name);
            }
            else if (!string.Equals(previous.Checksum, script.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(script.Name);
                _logger.LogWarning(
                    "Le script de migration {Script} a été modifié après son application (somme de contrôle {Recorded} enregistrée, {Current} actuelle). Il n'est pas rejoué : ajoutez un nouveau script.",
                    script.Name, previous.Checksum, script.Checksum);
            }
        }

        if (appliedNow.Count == 0)
            _logger.LogInformation("Schéma à jour : aucune migration à appliquer.");
        return new SchemaMigrationResult(appliedNow, mismatches) { AlreadyUpToDate = scripts.Count - appliedNow.Count };
    }

    /// <summary>La fermeture de la connexion libère de toute façon un verrou de session.</summary>
    private async Task ReleaseLockAsync(ISchemaMigrationConnection connection)
    {
        try
        {
            await connection.ReleaseLockAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Libération du verrou de migration impossible.");
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
