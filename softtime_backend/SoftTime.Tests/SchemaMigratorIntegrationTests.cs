using Microsoft.Data.SqlClient;

using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Tests;

/// <summary>
/// Test ignoré si la variable d'environnement SOFTTIME_MIGTEST_CONN n'existe pas. Sa valeur
/// (authentification Windows, jamais écrite dans le dépôt) doit viser une base JETABLE dont le
/// nom commence par SOFTTIME_MIGTEST_ ; toute autre base est refusée.
/// </summary>
public sealed class MigrationIntegrationFactAttribute : FactAttribute
{
    public const string Variable = "SOFTTIME_MIGTEST_CONN";

    public MigrationIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Variable d'environnement {Variable} absente : test d'intégration des migrations ignoré.";
    }
}

[Trait("Category", "Integration")]
public class SchemaMigratorIntegrationTests
{
    // Les 48 tables du modèle EF (SoftTimeDbContext) créées par 001, plus suivi_JOURNAL créée par 003.
    private static readonly string[] ApplicationTables =
    {
        "T_ROLE", "T_RESPONSABLE", "T_DROIT", "T_DROIT_ROLE", "T_BDD_AUTORISE", "T_USER_PERMISSION",
        "T_BDD_SAGE", "T_BDD_POINTEUSE", "T_CARDPAIE", "T_CATEGORIE", "T_CAT_SAL", "T_TOLERANCES",
        "T_POINTAGE", "T_HEURECORRIGER", "T_HEUREANOMALIE", "T_HEUREDIMANCHE", "T_HS_VALIDER", "T_HSExoImp",
        "T_HSSemaine", "T_HSSAL", "T_REST_FERIE", "T_MAJORATION", "T_PLANNING", "T_PLANNING_SAL",
        "T_FERIE", "T_CODEABSENCE", "T_CLOCK", "T_ACTIVECORRESP", "T_Semaine", "T_SemainePeriode",
        "T_impr_ABS", "T_impr_RET", "T_impr_HS", "T_FRAIS", "T_CANTINE", "suivi_SUIVI", "suivi_T",
        "suivi_TBLCLN", "T_FIELD_ROLE", "T_SOURCE_ENTITY_MAPPING", "T_SOURCE_FIELD_MAPPING",
        "T_CODE_CONSTANTE", "T_PAIE_ECRITURE", "T_HISTORIQUE_POINTAGE", "T_HISTORIQUE_ANOMALIE",
        "T_HISTORIQUE_HS", "T_ROLE_PERMISSION", "T_AUDIT_DROIT", "suivi_JOURNAL",
    };

    [MigrationIntegrationFact]
    public async Task Run_CreatesOrKeepsSchema_AndSecondRunAppliesNothing()
    {
        var connectionString = DisposableConnectionString();
        var migrator = Migrator(connectionString, lockTimeoutSeconds: 60);

        await migrator.RunAsync();
        var second = await migrator.RunAsync();

        Assert.Empty(second.Applied);
        Assert.Empty(second.ChecksumMismatches);

        var initial = new EmbeddedMigrationScriptSource().Load().Single(s => s.Version == 1);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(initial.Checksum, await ScalarAsync<string>(connection,
            "SELECT RTRIM(SOMME_CONTROLE) FROM dbo.T_SCHEMA_VERSION WHERE VERSION = 1;"));
        foreach (var table in ApplicationTables)
            Assert.True(await ScalarAsync<int>(connection, $"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}', N'U') IS NULL THEN 0 ELSE 1 END;") == 1,
                $"Table dbo.{table} absente après la migration.");
        Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.T_ROLE WHERE IDROLE = 1;"));
        Assert.Equal(6, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.T_DROIT WHERE IDDROIT BETWEEN 1 AND 6;"));
    }

    [MigrationIntegrationFact]
    public async Task Run_WhenLockIsHeldElsewhere_FailsWithClearMessage()
    {
        var connectionString = DisposableConnectionString();
        await using var holder = new SqlConnection(connectionString);
        await holder.OpenAsync();
        await using (var take = new SqlCommand(
            "EXEC sys.sp_getapplock @Resource = N'SoftTime.SchemaMigration', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0;", holder))
            await take.ExecuteNonQueryAsync();

        var ex = await Assert.ThrowsAsync<SchemaMigrationException>(() => Migrator(connectionString, lockTimeoutSeconds: 1).RunAsync());

        Assert.Contains("verrou", ex.Message);
        Assert.DoesNotContain("Data Source", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [MigrationIntegrationFact]
    public async Task Script002_CanBeReplayedOnAMigratedDatabase_WithoutChangingTheSchema()
    {
        var connectionString = DisposableConnectionString();
        await Migrator(connectionString, lockTimeoutSeconds: 60).RunAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        const string fingerprint = "SELECT COUNT(*) FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables)";
        var before = await ScalarAsync<int>(connection, fingerprint);
        var script = new EmbeddedMigrationScriptSource().Load().Single(s => s.Version == 2);
        foreach (var batch in SqlBatchSplitter.Split(script.Content))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(before, await ScalarAsync<int>(connection, fingerprint));
        Assert.Equal(3, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.T_SCHEMA_VERSION WHERE VERSION IN (1, 2, 3);"));
    }

    [MigrationIntegrationFact]
    public async Task Script003_CreatesTheJournalWithTheEntityColumns_AndCanBeReplayedWithoutChange()
    {
        var connectionString = DisposableConnectionString();
        await Migrator(connectionString, lockTimeoutSeconds: 60).RunAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        const string fingerprint = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.suivi_JOURNAL')";
        const string columns = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.suivi_JOURNAL')";
        var indexesBefore = await ScalarAsync<int>(connection, fingerprint);
        Assert.Equal(13, await ScalarAsync<int>(connection, columns));
        Assert.Equal(7, indexesBefore);
        var script = new EmbeddedMigrationScriptSource().Load().Single(s => s.Version == 3);
        foreach (var batch in SqlBatchSplitter.Split(script.Content))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(indexesBefore, await ScalarAsync<int>(connection, fingerprint));
        Assert.Equal(13, await ScalarAsync<int>(connection, columns));
    }

    private static SchemaMigrator Migrator(string connectionString, int lockTimeoutSeconds)
        => new(new SqlSchemaMigrationConnectionFactory(connectionString),
            new EmbeddedMigrationScriptSource(),
            new SchemaMigrationOptions { AutoMigrate = true, LockTimeoutSeconds = lockTimeoutSeconds });

    private static string DisposableConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(MigrationIntegrationFactAttribute.Variable)!;
        var database = new SqlConnectionStringBuilder(value).InitialCatalog;
        Assert.True(database.StartsWith("SOFTTIME_MIGTEST_", StringComparison.OrdinalIgnoreCase),
            "SOFTTIME_MIGTEST_CONN doit viser une base jetable SOFTTIME_MIGTEST_*.");
        return value;
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }
}
