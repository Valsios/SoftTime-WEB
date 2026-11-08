using Microsoft.Extensions.Options;

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

        batches.Should().Equal(new[] { "SELECT 1;", "SELECT 2;", "SELECT 3;" });
    }

    [Fact]
    public void Split_KeepsGoInsideIdentifiersAndBlockComments()
    {
        var script = "SELECT 1 AS GOAL;\nGO\n/* commentaire\nGO\nfin */\nSELECT 2;\nGO";

        var batches = SqlBatchSplitter.Split(script);

        batches.Count.Should().Be(2);
        batches[0].Should().Be("SELECT 1 AS GOAL;");
        batches[1].Should().Contain("GO\nfin */");
    }

    [Fact]
    public void Split_WithRepeatCount_RepeatsBatch()
    {
        SqlBatchSplitter.Split("SELECT 1;\nGO 3").Should().Equal(new[] { "SELECT 1;", "SELECT 1;", "SELECT 1;" });
    }

    [Fact]
    public void EmbeddedInitialSchema_IsLoadedAndContainsNoDestructiveStatement()
    {
        var scripts = new EmbeddedMigrationScriptSource().Load();

        var initial = scripts.Should().ContainSingle(s => s.Version == 1).Which;
        initial.Name.Should().Be("001_schema_initial.sql");
        (SqlBatchSplitter.Split(initial.Content).Count > 40).Should().BeTrue();
        foreach (var forbidden in new[] { "DROP TABLE", "DROP INDEX", "DROP COLUMN", "DROP CONSTRAINT", "DELETE FROM", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "INSERT INTO dbo.T_RESPONSABLE" })
            initial.Content.Should().NotContainEquivalentOf(forbidden);
    }

    // ---------- Ordre numérique ----------

    [Fact]
    public async Task Run_AppliesPendingScriptsInNumericOrder()
    {
        var connection = new FakeConnection();
        var migrator = Migrator(connection, Script(10, "010_c.sql"), Script(2, "002_b.sql"), Script(1, "001_a.sql"));

        var result = await migrator.RunAsync();

        result.Applied.Should().Equal(new[] { "001_a.sql", "002_b.sql", "010_c.sql" });
        connection.Recorded.Select(r => r.Version).Should().Equal(new[] { 1, 2, 10 });
        connection.Commits.Should().Be(3);
        connection.VersionTableEnsured.Should().BeTrue();
        connection.LockReleased.Should().BeTrue();
    }

    [Fact]
    public async Task Run_SkipsAlreadyAppliedScripts()
    {
        var first = Script(1, "001_a.sql");
        var connection = new FakeConnection { Applied = { new AppliedMigration(1, first.Name, first.Checksum) } };

        var result = await Migrator(connection, first, Script(2, "002_b.sql")).RunAsync();

        result.Applied.Should().Equal(new[] { "002_b.sql" });
        connection.Executed.Should().NotContain(sql => sql.Contains("001_a"));
    }

    [Fact]
    public void Order_WithDuplicateVersion_Throws()
    {
        var ex = FluentActions.Invoking(() =>
            MigrationScriptNames.Order(new[] { Script(1, "001_a.sql"), Script(1, "001_b.sql") })).Should().ThrowExactly<InvalidOperationException>().Which;
        ex.Message.Should().Contain("001");
    }

    [Theory]
    [InlineData("001_schema_initial.sql", true, 1)]
    [InlineData("012_ajout_index.sql", true, 12)]
    [InlineData("1_court.sql", false, 0)]
    [InlineData("001-tiret.sql", false, 0)]
    [InlineData("000_zero.sql", false, 0)]
    public void TryParseVersion_FollowsNamingConvention(string name, bool valid, int version)
    {
        MigrationScriptNames.TryParseVersion(name, out var parsed).Should().Be(valid);
        parsed.Should().Be(version);
    }

    // ---------- Somme de contrôle ----------

    [Fact]
    public async Task Run_WithModifiedAppliedScript_WarnsWithoutReexecuting()
    {
        var script = Script(1, "001_a.sql");
        var connection = new FakeConnection { Applied = { new AppliedMigration(1, script.Name, new string('0', 64)) } };

        var result = await Migrator(connection, script).RunAsync();

        result.Applied.Should().BeEmpty();
        result.ChecksumMismatches.Should().Equal(new[] { "001_a.sql" });
        connection.Executed.Should().BeEmpty();
        connection.Recorded.Should().BeEmpty();
    }

    [Fact]
    public void Checksum_IsSha256AndIgnoresLineEndings()
    {
        var lf = MigrationScript.ComputeChecksum("SELECT 1;\nGO\n");
        lf.Length.Should().Be(64);
        MigrationScript.ComputeChecksum("SELECT 1;\r\nGO\r\n").Should().Be(lf);
        MigrationScript.ComputeChecksum("SELECT 2;\nGO\n").Should().NotBe(lf);
    }

    // ---------- Verrou ----------

    [Fact]
    public async Task Run_WhenLockIsRefused_ThrowsClearMessageAndExecutesNothing()
    {
        var connection = new FakeConnection { LockGranted = false };
        var migrator = Migrator(connection, new SchemaMigrationOptions { AutoMigrate = true, LockTimeoutSeconds = 5 }, Script(1, "001_a.sql"));

        var ex = (await FluentActions.Awaiting(() => migrator.RunAsync()).Should().ThrowExactlyAsync<SchemaMigrationException>()).Which;

        ex.Message.Should().Contain("verrou");
        ex.Message.Should().Contain("5 s");
        connection.RequestedLockTimeout.Should().Be(5);
        connection.VersionTableEnsured.Should().BeFalse();
        connection.Executed.Should().BeEmpty();
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

        var ex = (await FluentActions.Awaiting(() => migrator.RunAsync()).Should().ThrowExactlyAsync<SchemaMigrationException>()).Which;

        ex.Message.Should().Contain("002_b.sql");
        ex.Message.Should().Contain("lot 2 sur 3");
        ex.Message.Should().Contain("annulée");
        ex.Message.Should().NotContainEquivalentOf("Data Source");
        connection.Rollbacks.Should().Be(1);
        connection.Commits.Should().Be(1);
        connection.Recorded.Select(r => r.Version).Should().Equal(new[] { 1 });
        connection.Executed.Should().NotContain(sql => sql.Contains("003_c") || sql == "SELECT 3;");
        connection.LockReleased.Should().BeTrue();
    }

    // ---------- Option désactivée ----------

    [Fact]
    public async Task RunIfEnabled_WhenDisabled_OpensNoConnection()
    {
        var factory = new FakeFactory(new FakeConnection());
        var migrator = new SchemaMigrator(factory, new ListSource(Script(1, "001_a.sql")), Options.Create(new SchemaMigrationOptions()));

        var result = await migrator.RunIfEnabledAsync();

        result.Should().BeNull();
        factory.Opened.Should().Be(0);
    }

    // ---------- Garde des méthodes Ensure* ----------

    [Fact]
    public async Task EnsureStorage_OnDatabaseWithoutSchema_ThrowsClearMessage()
    {
        var uow = new MissingSchemaUnitOfWork();

        var ex = (await FluentActions.Awaiting(() =>
            PunchProcessingConfiguration.EnsureStorageAsync(uow, CancellationToken.None)).Should().ThrowExactlyAsync<DomainException>()).Which;

        ex.Message.Should().Be(SchemaPresenceGuard.MissingSchemaMessage);
        uow.LastSql.Should().Contain("OBJECT_ID(N'dbo.T_HEURECORRIGER', N'U') IS NULL");
    }

    [Fact]
    public async Task EnsureAuditJournal_WithoutJournalTable_ThrowsClearFrenchMessage()
    {
        var uow = new MissingSchemaUnitOfWork();

        var ex = (await FluentActions.Awaiting(() =>
            SchemaPresenceGuard.EnsureAuditJournalAsync(uow, CancellationToken.None)).Should().ThrowExactlyAsync<DomainException>()).Which;

        ex.Message.Should().Be("Le journal d'audit est absent : activez Database:AutoMigrate (développement) ou exécutez migrations/versions/003_journal_central.sql.");
        uow.LastSql.Should().Contain("OBJECT_ID(N'dbo.suivi_JOURNAL', N'U') IS NULL");
    }

    [Fact]
    public void GuardedSql_KeepsOriginalStatementAfterTheCheck()
    {
        var sql = SchemaPresenceGuard.GuardedSql("T_ROLE", "SELECT 1;");

        (sql.IndexOf("THROW", StringComparison.Ordinal) < sql.IndexOf("SELECT 1;", StringComparison.Ordinal)).Should().BeTrue();
    }

    // ---------- Doubles de test ----------

    private static MigrationScript Script(int version, string name) => new(version, name, $"-- {name}\nSELECT {version};\nGO\n");

    private static SchemaMigrator Migrator(FakeConnection connection, params MigrationScript[] scripts)
        => Migrator(connection, new SchemaMigrationOptions { AutoMigrate = true }, scripts);

    private static SchemaMigrator Migrator(FakeConnection connection, SchemaMigrationOptions options, params MigrationScript[] scripts)
        => new(new FakeFactory(connection), new ListSource(scripts), Options.Create(options));

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
            _inTransaction.Should().BeTrue("Chaque lot doit s'exécuter dans la transaction du script.");
            if (FailOn != null && sql.Contains(FailOn))
                throw new InvalidOperationException("Nom d'objet non valide.");
            _pending.Add(sql);
            return Task.CompletedTask;
        }

        public Task RecordAppliedAsync(MigrationScript script, string appliedBy, CancellationToken ct)
        {
            string.IsNullOrWhiteSpace(appliedBy).Should().BeFalse();
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
