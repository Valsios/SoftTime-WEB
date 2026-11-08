using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Security;
using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Tests;

/// <summary>Migration 002 (intégrité d'une base neuve) et lignes de journal du démarrage.</summary>
public class SchemaMigrationsIntegrityTests
{
    // ---------- Script 002 ----------

    [Fact]
    public void Script002_IsEmbeddedAfter001_AndIsReplayableAndNonDestructive()
    {
        // Act
        var scripts = new EmbeddedMigrationScriptSource().Load();

        // Assert
        var integrity = scripts.Should().ContainSingle(s => s.Version == 2).Which;
        integrity.Name.Should().Be("002_integrite_base_neuve.sql");
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "ALTER TABLE", "USE [", "CREATE DATABASE", "UPDATE " })
            integrity.Content.Should().NotContainEquivalentOf(forbidden);
        // Rejouable : l'index n'est créé que s'il n'existe pas, et seulement si les colonnes sont indexables.
        integrity.Content.Should().Contain("name = N'UX_T_ACTIVECORRESP_PAIRE'");
        integrity.Content.Should().Contain("max_length = -1");
        integrity.Content.Should().Contain("PRINT");
        // Seules des créations d'index uniques : aucun index non unique (performance) n'est ajouté ici.
        integrity.Content.Should().NotContainEquivalentOf("CREATE INDEX");
        integrity.Content.Split('\n').Where(l => l.Contains("CREATE ", StringComparison.OrdinalIgnoreCase) && !l.TrimStart().StartsWith("--") && !l.TrimStart().StartsWith("PRINT")).Should().AllSatisfy(line => line.Replace("EXEC (N'", string.Empty).Should().ContainEquivalentOf("UNIQUE INDEX"));
        integrity.Content.Should().NotContainEquivalentOf("Password");
    }

    // ---------- Script 003 ----------

    [Fact]
    public void Script003_IsEmbeddedAfter002_AndIsReplayableAndNonDestructive()
    {
        // Act
        var scripts = new EmbeddedMigrationScriptSource().Load();

        // Assert
        scripts.Select(s => s.Version).ToArray().Should().Equal(new[] { 1, 2, 3, 4, 5 });
        var journal = scripts.Should().ContainSingle(s => s.Version == 3).Which;
        journal.Name.Should().Be("003_journal_central.sql");
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "ALTER TABLE", "USE [", "CREATE DATABASE", "UPDATE ", "INSERT " })
            journal.Content.Should().NotContainEquivalentOf(forbidden);
        // Rejouable : table et index ne sont créés que s'ils n'existent pas.
        journal.Content.Should().Contain("OBJECT_ID(N'dbo.suivi_JOURNAL', N'U') IS NULL");
        foreach (var index in new[] { "IX_suivi_JOURNAL_Date", "IX_suivi_JOURNAL_Base_Date", "IX_suivi_JOURNAL_Table_Action_Date",
                     "IX_suivi_JOURNAL_Correlation", "IX_suivi_JOURNAL_User_Date", "IX_suivi_JOURNAL_Cles_Date" })
            journal.Content.Should().Contain($"name = N'{index}'");
        journal.Content.Should().NotContainEquivalentOf("UNIQUE INDEX");
        journal.Content.Should().Contain("PRINT");
        // Aucune autre table n'est touchée (hors en-tête de commentaire qui les cite pour le dire).
        var code = journal.Content[(journal.Content.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        foreach (var other in new[] { "suivi_SUIVI", "suivi_TBLCLN", "T_AUDIT_DROIT", "TRIGGER" })
            code.Should().NotContainEquivalentOf(other);
        journal.Content.Should().NotContainEquivalentOf("Password");
    }

    // ---------- Script 004 ----------

    [Fact]
    public void Script004_AddsAccountSecurityStorage_OnlyByAdditions()
    {
        // Act
        var scripts = new EmbeddedMigrationScriptSource().Load();

        // Assert
        var security = scripts.Should().ContainSingle(s => s.Version == 4).Which;
        security.Name.Should().Be("004_securite_comptes.sql");
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "UPDATE ", "INSERT " })
            security.Content.Should().NotContainEquivalentOf(forbidden);
        // Rejouable : chaque objet est testé avant création.
        security.Content.Should().Contain("OBJECT_ID(N'dbo.T_REFRESH_TOKEN', N'U') IS NULL");
        security.Content.Should().Contain("COL_LENGTH(N'dbo.T_RESPONSABLE', N'TENTATIVES_ECHOUEES') IS NULL");
        security.Content.Should().Contain("COL_LENGTH(N'dbo.T_RESPONSABLE', N'VERROUILLE_JUSQUA') IS NULL");
        security.Content.Should().Contain("name = N'UX_T_REFRESH_TOKEN_Hash'");
        // Seule l'empreinte du jeton est stockée.
        security.Content.Should().Contain("TOKEN_HASH        char(64)");
        security.Content.Should().NotContainEquivalentOf("Password");
    }

    // ---------- Script 005 ----------

    [Fact]
    public void Script005_LoginUniqueness_OnlyByAdditions_AndSkipsIndexWhenDuplicatesExist()
    {
        // Act
        var scripts = new EmbeddedMigrationScriptSource().Load();

        // Assert
        var unicity = scripts.Should().ContainSingle(s => s.Version == 5).Which;
        unicity.Name.Should().Be("005_unicite_login.sql");
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "UPDATE ", "INSERT ", "STRING_AGG" })
            unicity.Content.Should().NotContainEquivalentOf(forbidden);
        // Rejouable : colonne et index testés avant création.
        unicity.Content.Should().Contain("COL_LENGTH(N'dbo.T_RESPONSABLE', N'LOGIN_CLE') IS NULL");
        unicity.Content.Should().Contain($"name = N'{LoginKey.UniqueIndexName}'");
        // Même expression que LoginKey : sans espaces en bordure, en majuscules.
        unicity.Content.Should().Contain("UPPER(LTRIM(RTRIM(LOGIN)))");
        // Des doublons existants n'arrêtent pas le démarrage : l'index est seulement ignoré, avec un message.
        unicity.Content.Should().Contain("IF @doublons IS NULL");
        unicity.Content.Should().Contain("index unique NON créé");
    }

    // ---------- Journal de démarrage ----------

    [Fact]
    public async Task RunIfEnabledAsync_MixedScripts_LogsAppliedAndUpToDateCounts()
    {
        // Arrange
        var logger = new ListLogger();
        var first = Script(1, "001_a.sql");
        var second = Script(2, "002_b.sql");
        var fresh = new FakeConnection();

        // Act
        await new SchemaMigrator(new FakeFactory(fresh), new ListSource(first, second), Options.Create(new SchemaMigrationOptions { AutoMigrate = true }), logger)
            .RunIfEnabledAsync();

        // Assert
        logger.Messages.Should().Contain("Migrations de schéma : 2 script(s) appliqué(s), 0 déjà à jour.");
        var upToDate = new FakeConnection();
        upToDate.Applied.Add(new AppliedMigration(1, first.Name, first.Checksum));
        upToDate.Applied.Add(new AppliedMigration(2, second.Name, second.Checksum));
        await new SchemaMigrator(new FakeFactory(upToDate), new ListSource(first, second), Options.Create(new SchemaMigrationOptions { AutoMigrate = true }), logger)
            .RunIfEnabledAsync();
        logger.Messages.Should().Contain("Migrations de schéma : 0 script(s) appliqué(s), 2 déjà à jour.");
    }

    [Fact]
    public async Task RunIfEnabled_WhenDisabled_LogsTheDisabledMessageWithoutOpeningAConnection()
    {
        // Arrange
        var logger = new ListLogger();
        var factory = new FakeFactory(new FakeConnection());

        // Act
        await new SchemaMigrator(factory, new ListSource(Script(1, "001_a.sql")), Options.Create(new SchemaMigrationOptions()), logger).RunIfEnabledAsync();

        // Assert
        factory.Opened.Should().Be(0);
        logger.Messages.Should().Contain("Migrations de schéma désactivées (Database:AutoMigrate = false).");
        logger.Messages.Should().AllSatisfy(m => m.Should().NotContainEquivalentOf("Data Source"));
    }

    private static MigrationScript Script(int version, string name)
        => new(version, name, $"-- {name}\nSELECT {version};\nGO\n");

    private sealed class ListLogger : ILogger<SchemaMigrator>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

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
        public List<AppliedMigration> Applied { get; } = new();

        public Task<bool> TryAcquireLockAsync(int timeoutSeconds, CancellationToken ct) => Task.FromResult(true);
        public Task ReleaseLockAsync(CancellationToken ct) => Task.CompletedTask;
        public Task EnsureVersionTableAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AppliedMigration>>(Applied.ToList());
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteBatchAsync(string sql, CancellationToken ct) => Task.CompletedTask;
        public Task RecordAppliedAsync(MigrationScript script, string appliedBy, CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
