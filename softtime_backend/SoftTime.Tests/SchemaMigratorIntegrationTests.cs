using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Tests;

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

        second.Applied.Should().BeEmpty();
        second.ChecksumMismatches.Should().BeEmpty();

        var initial = new EmbeddedMigrationScriptSource().Load().Single(s => s.Version == 1);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        (await ScalarAsync<string>(connection,
            "SELECT RTRIM(SOMME_CONTROLE) FROM dbo.T_SCHEMA_VERSION WHERE VERSION = 1;")).Should().Be(initial.Checksum);
        foreach (var table in ApplicationTables)
            (await ScalarAsync<int>(connection, $"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}', N'U') IS NULL THEN 0 ELSE 1 END;") == 1).Should().BeTrue($"Table dbo.{table} absente après la migration.");
        (await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.T_ROLE WHERE IDROLE = 1;")).Should().Be(1);
        (await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.T_DROIT WHERE IDDROIT BETWEEN 1 AND 6;")).Should().Be(6);
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

        var ex = (await FluentActions.Awaiting(() => Migrator(connectionString, lockTimeoutSeconds: 1).RunAsync()).Should().ThrowExactlyAsync<SchemaMigrationException>()).Which;

        ex.Message.Should().Contain("verrou");
        ex.Message.Should().NotContainEquivalentOf("Data Source");
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

        (await ScalarAsync<int>(connection, fingerprint)).Should().Be(before);
        (await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.T_SCHEMA_VERSION WHERE VERSION IN (1, 2, 3);")).Should().Be(3);
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
        (await ScalarAsync<int>(connection, columns)).Should().Be(13);
        indexesBefore.Should().Be(7);
        var script = new EmbeddedMigrationScriptSource().Load().Single(s => s.Version == 3);
        foreach (var batch in SqlBatchSplitter.Split(script.Content))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        (await ScalarAsync<int>(connection, fingerprint)).Should().Be(indexesBefore);
        (await ScalarAsync<int>(connection, columns)).Should().Be(13);
    }

    private static SchemaMigrator Migrator(string connectionString, int lockTimeoutSeconds)
        => new(new SqlSchemaMigrationConnectionFactory(connectionString),
            new EmbeddedMigrationScriptSource(),
            Options.Create(new SchemaMigrationOptions { AutoMigrate = true, LockTimeoutSeconds = lockTimeoutSeconds }));

    private static string DisposableConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(MigrationIntegrationFactAttribute.Variable)!;
        var database = new SqlConnectionStringBuilder(value).InitialCatalog;
        database.StartsWith("SOFTTIME_MIGTEST_", StringComparison.OrdinalIgnoreCase).Should().BeTrue("SOFTTIME_MIGTEST_CONN doit viser une base jetable SOFTTIME_MIGTEST_*.");
        return value;
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }
}
