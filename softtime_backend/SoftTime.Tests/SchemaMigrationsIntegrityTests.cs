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
        var scripts = new EmbeddedMigrationScriptSource().Load();
        var integrity = Assert.Single(scripts, s => s.Version == 2);

        Assert.Equal("002_integrite_base_neuve.sql", integrity.Name);
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "ALTER TABLE", "USE [", "CREATE DATABASE", "UPDATE " })
            Assert.DoesNotContain(forbidden, integrity.Content, StringComparison.OrdinalIgnoreCase);
        // Rejouable : l'index n'est créé que s'il n'existe pas, et seulement si les colonnes sont indexables.
        Assert.Contains("name = N'UX_T_ACTIVECORRESP_PAIRE'", integrity.Content);
        Assert.Contains("max_length = -1", integrity.Content);
        Assert.Contains("PRINT", integrity.Content);
        // Seules des créations d'index uniques : aucun index non unique (performance) n'est ajouté ici.
        Assert.DoesNotContain("CREATE INDEX", integrity.Content, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            integrity.Content.Split('\n').Where(l => l.Contains("CREATE ", StringComparison.OrdinalIgnoreCase) && !l.TrimStart().StartsWith("--") && !l.TrimStart().StartsWith("PRINT")),
            line => Assert.Contains("UNIQUE INDEX", line.Replace("EXEC (N'", string.Empty), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Password", integrity.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Script 003 ----------

    [Fact]
    public void Script003_IsEmbeddedAfter002_AndIsReplayableAndNonDestructive()
    {
        var scripts = new EmbeddedMigrationScriptSource().Load();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, scripts.Select(s => s.Version).ToArray());
        var journal = Assert.Single(scripts, s => s.Version == 3);

        Assert.Equal("003_journal_central.sql", journal.Name);
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "ALTER TABLE", "USE [", "CREATE DATABASE", "UPDATE ", "INSERT " })
            Assert.DoesNotContain(forbidden, journal.Content, StringComparison.OrdinalIgnoreCase);
        // Rejouable : table et index ne sont créés que s'ils n'existent pas.
        Assert.Contains("OBJECT_ID(N'dbo.suivi_JOURNAL', N'U') IS NULL", journal.Content);
        foreach (var index in new[] { "IX_suivi_JOURNAL_Date", "IX_suivi_JOURNAL_Base_Date", "IX_suivi_JOURNAL_Table_Action_Date",
                     "IX_suivi_JOURNAL_Correlation", "IX_suivi_JOURNAL_User_Date", "IX_suivi_JOURNAL_Cles_Date" })
            Assert.Contains($"name = N'{index}'", journal.Content);
        Assert.DoesNotContain("UNIQUE INDEX", journal.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRINT", journal.Content);
        // Aucune autre table n'est touchée (hors en-tête de commentaire qui les cite pour le dire).
        var code = journal.Content[(journal.Content.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        foreach (var other in new[] { "suivi_SUIVI", "suivi_TBLCLN", "T_AUDIT_DROIT", "TRIGGER" })
            Assert.DoesNotContain(other, code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", journal.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Script 004 ----------

    [Fact]
    public void Script004_AddsAccountSecurityStorage_OnlyByAdditions()
    {
        var scripts = new EmbeddedMigrationScriptSource().Load();
        var security = Assert.Single(scripts, s => s.Version == 4);

        Assert.Equal("004_securite_comptes.sql", security.Name);
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "UPDATE ", "INSERT " })
            Assert.DoesNotContain(forbidden, security.Content, StringComparison.OrdinalIgnoreCase);
        // Rejouable : chaque objet est testé avant création.
        Assert.Contains("OBJECT_ID(N'dbo.T_REFRESH_TOKEN', N'U') IS NULL", security.Content);
        Assert.Contains("COL_LENGTH(N'dbo.T_RESPONSABLE', N'TENTATIVES_ECHOUEES') IS NULL", security.Content);
        Assert.Contains("COL_LENGTH(N'dbo.T_RESPONSABLE', N'VERROUILLE_JUSQUA') IS NULL", security.Content);
        Assert.Contains("name = N'UX_T_REFRESH_TOKEN_Hash'", security.Content);
        // Seule l'empreinte du jeton est stockée.
        Assert.Contains("TOKEN_HASH        char(64)", security.Content);
        Assert.DoesNotContain("Password", security.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Script 005 ----------

    [Fact]
    public void Script005_LoginUniqueness_OnlyByAdditions_AndSkipsIndexWhenDuplicatesExist()
    {
        var scripts = new EmbeddedMigrationScriptSource().Load();
        var unicity = Assert.Single(scripts, s => s.Version == 5);

        Assert.Equal("005_unicite_login.sql", unicity.Name);
        foreach (var forbidden in new[] { "DROP ", "DELETE", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "UPDATE ", "INSERT ", "STRING_AGG" })
            Assert.DoesNotContain(forbidden, unicity.Content, StringComparison.OrdinalIgnoreCase);
        // Rejouable : colonne et index testés avant création.
        Assert.Contains("COL_LENGTH(N'dbo.T_RESPONSABLE', N'LOGIN_CLE') IS NULL", unicity.Content);
        Assert.Contains($"name = N'{LoginKey.UniqueIndexName}'", unicity.Content);
        // Même expression que LoginKey : sans espaces en bordure, en majuscules.
        Assert.Contains("UPPER(LTRIM(RTRIM(LOGIN)))", unicity.Content);
        // Des doublons existants n'arrêtent pas le démarrage : l'index est seulement ignoré, avec un message.
        Assert.Contains("IF @doublons IS NULL", unicity.Content);
        Assert.Contains("index unique NON créé", unicity.Content);
    }

    // ---------- Journal de démarrage ----------

    [Fact]
    public async Task RunIfEnabled_LogsHowManyScriptsWereAppliedAndHowManyWereUpToDate()
    {
        var logger = new ListLogger();
        var first = Script(1, "001_a.sql");
        var second = Script(2, "002_b.sql");

        var fresh = new FakeConnection();
        await new SchemaMigrator(new FakeFactory(fresh), new ListSource(first, second), Options.Create(new SchemaMigrationOptions { AutoMigrate = true }), logger)
            .RunIfEnabledAsync();
        Assert.Contains("Migrations de schéma : 2 script(s) appliqué(s), 0 déjà à jour.", logger.Messages);

        var upToDate = new FakeConnection();
        upToDate.Applied.Add(new AppliedMigration(1, first.Name, first.Checksum));
        upToDate.Applied.Add(new AppliedMigration(2, second.Name, second.Checksum));
        await new SchemaMigrator(new FakeFactory(upToDate), new ListSource(first, second), Options.Create(new SchemaMigrationOptions { AutoMigrate = true }), logger)
            .RunIfEnabledAsync();
        Assert.Contains("Migrations de schéma : 0 script(s) appliqué(s), 2 déjà à jour.", logger.Messages);
    }

    [Fact]
    public async Task RunIfEnabled_WhenDisabled_LogsTheDisabledMessageWithoutOpeningAConnection()
    {
        var logger = new ListLogger();
        var factory = new FakeFactory(new FakeConnection());

        await new SchemaMigrator(factory, new ListSource(Script(1, "001_a.sql")), Options.Create(new SchemaMigrationOptions()), logger).RunIfEnabledAsync();

        Assert.Equal(0, factory.Opened);
        Assert.Contains("Migrations de schéma désactivées (Database:AutoMigrate = false).", logger.Messages);
        Assert.All(logger.Messages, m => Assert.DoesNotContain("Data Source", m, StringComparison.OrdinalIgnoreCase));
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
