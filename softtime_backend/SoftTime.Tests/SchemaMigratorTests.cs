using Microsoft.Extensions.Configuration;
using SoftTime.Application.Services;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Tests;

public class SchemaMigratorTests
{
    // ---------- Découpage sur GO ----------

    [Fact]
    public void Split_OnGoLines_IgnoresCaseCommentsAndEmptyBatches()
    {
        var batches = SqlBatchSplitter.Split("SELECT 1;\r\nGO\r\n  go  \r\nSELECT 2;\nGo -- fin\nSELECT 3;");

        Assert.Equal(new[] { "SELECT 1;", "SELECT 2;", "SELECT 3;" }, batches);
    }

    [Fact]
    public void Split_KeepsGoInsideIdentifiersAndBlockComments()
    {
        var script = "SELECT 1 AS GOAL;\nGO\n/* commentaire\nGO\nfin */\nSELECT 2;\nGO";

        var batches = SqlBatchSplitter.Split(script);

        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT 1 AS GOAL;", batches[0]);
        Assert.Contains("GO\nfin */", batches[1]);
    }

    [Fact]
    public void Split_WithRepeatCount_RepeatsBatch()
    {
        Assert.Equal(new[] { "SELECT 1;", "SELECT 1;", "SELECT 1;" }, SqlBatchSplitter.Split("SELECT 1;\nGO 3"));
    }

    [Fact]
    public void EmbeddedInitialSchema_IsLoadedAndContainsNoDestructiveStatement()
    {
        var scripts = new EmbeddedMigrationScriptSource().Load();

        var initial = Assert.Single(scripts, s => s.Version == 1);
        Assert.Equal("001_schema_initial.sql", initial.Name);
        Assert.True(SqlBatchSplitter.Split(initial.Content).Count > 40);
        foreach (var forbidden in new[] { "DROP TABLE", "DROP INDEX", "DROP COLUMN", "DROP CONSTRAINT", "DELETE FROM", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "INSERT INTO dbo.T_RESPONSABLE" })
            Assert.DoesNotContain(forbidden, initial.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Ordre numérique ----------

    [Fact]
    public async Task Run_AppliesPendingScriptsInNumericOrder()
    {
        var connection = new FakeConnection();
        var migrator = Migrator(connection, Script(10, "010_c.sql"), Script(2, "002_b.sql"), Script(1, "001_a.sql"));

        var result = await migrator.RunAsync();

        Assert.Equal(new[] { "001_a.sql", "002_b.sql", "010_c.sql" }, result.Applied);
        Assert.Equal(new[] { 1, 2, 10 }, connection.Recorded.Select(r => r.Version));
        Assert.Equal(3, connection.Commits);
        Assert.True(connection.VersionTableEnsured);
        Assert.True(connection.LockReleased);
    }

    [Fact]
    public async Task Run_SkipsAlreadyAppliedScripts()
    {
        var first = Script(1, "001_a.sql");
        var connection = new FakeConnection { Applied = { new AppliedMigration(1, first.Name, first.Checksum) } };

        var result = await Migrator(connection, first, Script(2, "002_b.sql")).RunAsync();

        Assert.Equal(new[] { "002_b.sql" }, result.Applied);
        Assert.DoesNotContain(connection.Executed, sql => sql.Contains("001_a"));
    }

    [Fact]
    public void Order_WithDuplicateVersion_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MigrationScriptNames.Order(new[] { Script(1, "001_a.sql"), Script(1, "001_b.sql") }));
        Assert.Contains("001", ex.Message);
    }

    [Theory]
    [InlineData("001_schema_initial.sql", true, 1)]
    [InlineData("012_ajout_index.sql", true, 12)]
    [InlineData("1_court.sql", false, 0)]
    [InlineData("001-tiret.sql", false, 0)]
    [InlineData("000_zero.sql", false, 0)]
    public void TryParseVersion_FollowsNamingConvention(string name, bool valid, int version)
    {
        Assert.Equal(valid, MigrationScriptNames.TryParseVersion(name, out var parsed));
        Assert.Equal(version, parsed);
    }

    // ---------- Somme de contrôle ----------

    [Fact]
    public async Task Run_WithModifiedAppliedScript_WarnsWithoutReexecuting()
    {
        var script = Script(1, "001_a.sql");
        var connection = new FakeConnection { Applied = { new AppliedMigration(1, script.Name, new string('0', 64)) } };

        var result = await Migrator(connection, script).RunAsync();

        Assert.Empty(result.Applied);
        Assert.Equal(new[] { "001_a.sql" }, result.ChecksumMismatches);
        Assert.Empty(connection.Executed);
        Assert.Empty(connection.Recorded);
    }

    [Fact]
    public void Checksum_IsSha256AndIgnoresLineEndings()
    {
        var lf = MigrationScript.ComputeChecksum("SELECT 1;\nGO\n");
        Assert.Equal(64, lf.Length);
        Assert.Equal(lf, MigrationScript.ComputeChecksum("SELECT 1;\r\nGO\r\n"));
        Assert.NotEqual(lf, MigrationScript.ComputeChecksum("SELECT 2;\nGO\n"));
    }

    // ---------- Verrou ----------

    [Fact]
    public async Task Run_WhenLockIsRefused_ThrowsClearMessageAndExecutesNothing()
    {
        var connection = new FakeConnection { LockGranted = false };
        var migrator = Migrator(connection, new SchemaMigrationOptions { AutoMigrate = true, LockTimeoutSeconds = 5 }, Script(1, "001_a.sql"));

        var ex = await Assert.ThrowsAsync<SchemaMigrationException>(() => migrator.RunAsync());

        Assert.Contains("verrou", ex.Message);
        Assert.Contains("5 s", ex.Message);
        Assert.Equal(5, connection.RequestedLockTimeout);
        Assert.False(connection.VersionTableEnsured);
        Assert.Empty(connection.Executed);
    }

    // ---------- Échec d'un script ----------

    [Fact]
    public async Task Run_WhenBatchFails_RollsBackAndThrowsClearMessage()
    {
        var connection = new FakeConnection { FailOn = "ECHEC" };
        var migrator = Migrator(connection,
            Script(1, "001_a.sql"),
            new MigrationScript(2, "002_b.sql", "SELECT 1;\nGO\nSELECT ECHEC;\nGO\nSELECT 3;"),
            Script(3, "003_c.sql"));

        var ex = await Assert.ThrowsAsync<SchemaMigrationException>(() => migrator.RunAsync());

        Assert.Contains("002_b.sql", ex.Message);
        Assert.Contains("lot 2 sur 3", ex.Message);
        Assert.Contains("annulée", ex.Message);
        Assert.DoesNotContain("Data Source", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, connection.Rollbacks);
        Assert.Equal(1, connection.Commits);
        Assert.Equal(new[] { 1 }, connection.Recorded.Select(r => r.Version));
        Assert.DoesNotContain(connection.Executed, sql => sql.Contains("003_c") || sql == "SELECT 3;");
        Assert.True(connection.LockReleased);
    }

    // ---------- Option désactivée ----------

    [Fact]
    public async Task RunIfEnabled_WhenDisabled_OpensNoConnection()
    {
        var factory = new FakeFactory(new FakeConnection());
        var migrator = new SchemaMigrator(factory, new ListSource(Script(1, "001_a.sql")), new SchemaMigrationOptions());

        var result = await migrator.RunIfEnabledAsync();

        Assert.Null(result);
        Assert.Equal(0, factory.Opened);
    }

    [Fact]
    public void Options_DefaultToDisabledWithSixtySecondLock()
    {
        var options = SchemaMigrationOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.False(options.AutoMigrate);
        Assert.Equal(60, options.LockTimeoutSeconds);
    }

    [Fact]
    public void Options_AreReadFromDatabaseSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AutoMigrate"] = "true",
                ["Database:AutoMigrateLockTimeoutSeconds"] = "15",
            })
            .Build();

        var options = SchemaMigrationOptions.FromConfiguration(configuration);

        Assert.True(options.AutoMigrate);
        Assert.Equal(15, options.LockTimeoutSeconds);
    }

    // ---------- Garde des méthodes Ensure* ----------

    [Fact]
    public async Task EnsureStorage_OnDatabaseWithoutSchema_ThrowsClearMessage()
    {
        var uow = new MissingSchemaUnitOfWork();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PunchProcessingConfiguration.EnsureStorageAsync(uow, CancellationToken.None));

        Assert.Equal(SchemaPresenceGuard.MissingSchemaMessage, ex.Message);
        Assert.Contains("OBJECT_ID(N'dbo.T_HEURECORRIGER', N'U') IS NULL", uow.LastSql);
    }

    [Fact]
    public async Task EnsureAuditJournal_WithoutJournalTable_ThrowsClearFrenchMessage()
    {
        var uow = new MissingSchemaUnitOfWork();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SchemaPresenceGuard.EnsureAuditJournalAsync(uow, CancellationToken.None));

        Assert.Equal(
            "Le journal d'audit est absent : activez Database:AutoMigrate (développement) ou exécutez migrations/versions/003_journal_central.sql.",
            ex.Message);
        Assert.Contains("OBJECT_ID(N'dbo.suivi_JOURNAL', N'U') IS NULL", uow.LastSql);
    }

    [Fact]
    public void GuardedSql_KeepsOriginalStatementAfterTheCheck()
    {
        var sql = SchemaPresenceGuard.GuardedSql("T_ROLE", "SELECT 1;");

        Assert.True(sql.IndexOf("THROW", StringComparison.Ordinal) < sql.IndexOf("SELECT 1;", StringComparison.Ordinal));
    }

    // ---------- Doubles de test ----------

    private static MigrationScript Script(int version, string name) => new(version, name, $"-- {name}\nSELECT {version};\nGO\n");

    private static SchemaMigrator Migrator(FakeConnection connection, params MigrationScript[] scripts)
        => Migrator(connection, new SchemaMigrationOptions { AutoMigrate = true }, scripts);

    private static SchemaMigrator Migrator(FakeConnection connection, SchemaMigrationOptions options, params MigrationScript[] scripts)
        => new(new FakeFactory(connection), new ListSource(scripts), options);

    private sealed class ListSource(params MigrationScript[] scripts) : IMigrationScriptSource
    {
        public IReadOnlyList<MigrationScript> Load() => scripts;
    }

    private sealed class FakeFactory(FakeConnection connection) : ISchemaMigrationConnectionFactory
    {
        public int Opened { get; private set; }

        public Task<ISchemaMigrationConnection> OpenAsync(CancellationToken ct)
        {
            Opened++;
            return Task.FromResult<ISchemaMigrationConnection>(connection);
        }
    }

    private sealed class FakeConnection : ISchemaMigrationConnection
    {
        public bool LockGranted { get; init; } = true;
        public string? FailOn { get; init; }
        public List<AppliedMigration> Applied { get; } = new();
        public List<string> Executed { get; } = new();
        public List<MigrationScript> Recorded { get; } = new();
        public int? RequestedLockTimeout { get; private set; }
        public bool VersionTableEnsured { get; private set; }
        public bool LockReleased { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        private bool _inTransaction;
        private readonly List<string> _pending = new();

        public Task<bool> TryAcquireLockAsync(int timeoutSeconds, CancellationToken ct)
        {
            RequestedLockTimeout = timeoutSeconds;
            return Task.FromResult(LockGranted);
        }

        public Task ReleaseLockAsync(CancellationToken ct)
        {
            LockReleased = true;
            return Task.CompletedTask;
        }

        public Task EnsureVersionTableAsync(CancellationToken ct)
        {
            VersionTableEnsured = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AppliedMigration>>(Applied.ToList());

        public Task BeginTransactionAsync(CancellationToken ct)
        {
            _inTransaction = true;
            return Task.CompletedTask;
        }

        public Task ExecuteBatchAsync(string sql, CancellationToken ct)
        {
            Assert.True(_inTransaction, "Chaque lot doit s'exécuter dans la transaction du script.");
            if (FailOn != null && sql.Contains(FailOn))
                throw new InvalidOperationException("Nom d'objet non valide.");
            _pending.Add(sql);
            return Task.CompletedTask;
        }

        public Task RecordAppliedAsync(MigrationScript script, string appliedBy, CancellationToken ct)
        {
            Assert.False(string.IsNullOrWhiteSpace(appliedBy));
            Recorded.Add(script);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct)
        {
            Commits++;
            Executed.AddRange(_pending);
            _pending.Clear();
            _inTransaction = false;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            Rollbacks++;
            _pending.Clear();
            _inTransaction = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MissingSchemaUnitOfWork : IUnitOfWork
    {
        public string LastSql { get; private set; } = string.Empty;

        public IRepository<T> Repository<T>() where T : class => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default)
        {
            LastSql = sql;
            // Simule le THROW SQL de la garde (table cible absente).
            throw new InvalidOperationException("SOFTTIME_SCHEMA_ABSENT: dbo.T_HEURECORRIGER");
        }
    }
}
