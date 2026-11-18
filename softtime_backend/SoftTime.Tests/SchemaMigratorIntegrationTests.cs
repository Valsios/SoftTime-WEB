using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Tests;

[Trait("Category", "Integration")]
public class SchemaMigratorIntegrationTests
{
    // Les 48 tables du modèle EF (SoftTimeDbContext) créées par 001, plus suivi_JOURNAL créée par 003,
    // plus T_PAIE_VARIABLE et T_PAIE_ENVOI créées par 007, T_PAIE_OPTION par 008, les instantanés par 009.
    private static readonly string[] ApplicationTables =
    {
        "T_PAIE_VARIABLE", "T_PAIE_ENVOI", "T_PAIE_OPTION", "T_PAIE_INSTANTANE", "T_PAIE_INSTANTANE_LIGNE", "T_PAIE_CLOTURE",
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
    public async Task Script009_Snapshots_AreImmutable_AndProtectTheirSend()
    {
        var connectionString = DisposableConnectionString();
        await Migrator(connectionString, lockTimeoutSeconds: 60).RunAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var envoi = await ScalarAsync<int>(connection, """
            INSERT INTO dbo.T_PAIE_ENVOI (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, ENVOYE_LE, ENVOYE_PAR, STATUT, LIGNES, TYPE_BASE)
            VALUES (N'MIGTEST', '2026-01-01', '2026-01-31', SYSUTCDATETIME(), N'test', N'SUCCES', 1, N'STANDARD');
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        var instantane = await ScalarAsync<int>(connection, $"""
            INSERT INTO dbo.T_PAIE_INSTANTANE (ENVOI_ID, BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, TYPE_BASE, ENVOYE_LE, AVEC_FILTRE, REMISE_A_ZERO,
                HEURES_PAR_JOUR, VERSION_FORMAT, EMPREINTE, NB_LIGNES, NB_SANS_LIGNE, NB_NON_REMIS, CREE_LE)
            VALUES ({envoi}, N'MIGTEST', '2026-01-01', '2026-01-31', N'STANDARD', SYSUTCDATETIME(), 0, 1, 8, 1, REPLICATE('a', 64), 1, 0, 0, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        await ScalarAsync<int>(connection, $"""
            INSERT INTO dbo.T_PAIE_INSTANTANE_LIGNE (INSTANTANE_ID, MATRICULE, NUM_SAL, VARIABLE, DESTINATION, UNITE, VALEUR, REMISE_A_ZERO)
            VALUES ({instantane}, N'M1', 1, N'Exo130', N'HS01', N'H', 1.5, 0);
            SELECT 1;
            """);

        foreach (var interdit in new[]
                 {
                     "UPDATE dbo.T_PAIE_INSTANTANE SET EMPREINTE = REPLICATE('b', 64);",
                     "DELETE FROM dbo.T_PAIE_INSTANTANE;",
                     "UPDATE dbo.T_PAIE_INSTANTANE_LIGNE SET VALEUR = 9;",
                     "DELETE FROM dbo.T_PAIE_INSTANTANE_LIGNE;",
                 })
        {
            var ex = await FluentActions.Awaiting(() => ScalarAsync<int>(connection, interdit + " SELECT 1;")).Should().ThrowAsync<SqlException>();
            ex.Which.Number.Should().Be(51991, interdit);
        }
        await FluentActions.Awaiting(() => ScalarAsync<int>(connection, $"DELETE FROM dbo.T_PAIE_ENVOI WHERE ID = {envoi}; SELECT 1;"))
            .Should().ThrowAsync<SqlException>("la clé étrangère protège un envoi qui a un instantané");
        (await ScalarAsync<decimal>(connection, $"SELECT SUM(VALEUR) FROM dbo.T_PAIE_INSTANTANE_LIGNE WHERE INSTANTANE_ID = {instantane};")).Should().Be(1.5m);
        (await ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM dbo.T_PAIE_INSTANTANE WHERE ENVOI_ID = {envoi};")).Should().Be(1);
        // Statut de l'envoi toujours modifiable (INCERTAIN après un échec).
        (await ScalarAsync<int>(connection, $"UPDATE dbo.T_PAIE_ENVOI SET STATUT = N'INCERTAIN' WHERE ID = {envoi}; SELECT @@ROWCOUNT;")).Should().Be(1);
    }

    [MigrationIntegrationFact]
    public async Task Script009_EfCoreWritesASnapshotDespiteTheTriggers_AndTheVerificationHolds()
    {
        var connectionString = DisposableConnectionString();
        await Migrator(connectionString, lockTimeoutSeconds: 60).RunAsync();
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SoftTime.Infrastructure.Persistence.SoftTimeDbContext>()
            .UseSqlServer(connectionString).Options;
        var lignes = new SoftTime.Domain.Models.PayrollSnapshotLine[] { new("M1", 1, "Exo130", "HS01", "H", 1.5m, false), new("M2", 2, "Exo130", "HS01", "H", 0m, true) };

        int envoiId;
        await using (var db = new SoftTime.Infrastructure.Persistence.SoftTimeDbContext(options))
        {
            var uow = new SoftTime.Infrastructure.Repositories.UnitOfWork(db);
            var log = new SoftTime.Infrastructure.Services.Payroll.PayrollSendLog(new SingleScopeFactory(uow),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SoftTime.Infrastructure.Services.Payroll.PayrollSendLog>.Instance);
            envoiId = await log.RecordWithSnapshotAsync(
                new SoftTime.Application.Abstractions.Payroll.PayrollSend("MIGTEST_EF", "AUTRE", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), "rh", "SUCCES", 2),
                new SoftTime.Application.Abstractions.Payroll.PayrollSnapshotDraft(lignes, 0, 0, SoftTime.Application.Abstractions.Payroll.PayrollSendFilter.Aucun, true, 8m));
        }

        await using (var db = new SoftTime.Infrastructure.Persistence.SoftTimeDbContext(options))
        {
            var uow = new SoftTime.Infrastructure.Repositories.UnitOfWork(db);
            var entete = (await uow.Repository<SoftTime.Domain.Entities.SoftTime.PaieInstantane>().ListReadOnlyAsync(i => i.EnvoiId == envoiId)).Single();
            var relues = (await uow.Repository<SoftTime.Domain.Entities.SoftTime.PaieInstantaneLigne>().ListReadOnlyAsync(l => l.InstantaneId == entete.Id))
                .Select(l => new SoftTime.Domain.Models.PayrollSnapshotLine(l.Matricule, l.NumSal, l.Variable, l.Destination, l.Unite, l.Valeur, l.RemiseAZero))
                .ToList();
            SoftTime.Infrastructure.Services.Payroll.PayrollSnapshotService.Verifier(entete, relues).Integre.Should().BeTrue();
            entete.Empreinte.Should().Be(SoftTime.Domain.Models.PayrollSnapshotFingerprint.Calculer("MIGTEST_EF", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), "AUTRE", lignes));
        }
    }

    [MigrationIntegrationFact]
    public async Task Script010_Closure_IsDefinitive_NoUpdateNoDelete()
    {
        var connectionString = DisposableConnectionString();
        await Migrator(connectionString, lockTimeoutSeconds: 60).RunAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var suffixe = Guid.NewGuid().ToString("N")[..8];
        var envoi = await ScalarAsync<int>(connection, $"""
            INSERT INTO dbo.T_PAIE_ENVOI (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, ENVOYE_LE, ENVOYE_PAR, STATUT, LIGNES, TYPE_BASE)
            VALUES (N'MIGTEST_{suffixe}', '2026-03-01', '2026-03-31', SYSUTCDATETIME(), N'test', N'SUCCES', 1, N'STANDARD');
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        var instantane = await ScalarAsync<int>(connection, $"""
            INSERT INTO dbo.T_PAIE_INSTANTANE (ENVOI_ID, BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, TYPE_BASE, ENVOYE_LE, AVEC_FILTRE, REMISE_A_ZERO,
                HEURES_PAR_JOUR, VERSION_FORMAT, EMPREINTE, NB_LIGNES, NB_SANS_LIGNE, NB_NON_REMIS, CREE_LE)
            VALUES ({envoi}, N'MIGTEST_{suffixe}', '2026-03-01', '2026-03-31', N'STANDARD', SYSUTCDATETIME(), 0, 1, 8, 1, REPLICATE('a', 64), 0, 0, 0, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        var insertion = $"""
            INSERT INTO dbo.T_PAIE_CLOTURE (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, CLOTURE_LE, CLOTURE_PAR, ENVOI_ID, INSTANTANE_ID, EMPREINTE, STATUT_CONTROLE, RESUME_CONTROLE)
            VALUES (N'MIGTEST_{suffixe}', '2026-03-01', '2026-03-31', SYSUTCDATETIME(), N'test', {envoi}, {instantane}, REPLICATE('a', 64), N'Pret', N'r');
            SELECT 1;
            """;
        await ScalarAsync<int>(connection, insertion);

        foreach (var interdit in new[] { $"UPDATE dbo.T_PAIE_CLOTURE SET STATUT_CONTROLE = N'X' WHERE BDD_SAGE = N'MIGTEST_{suffixe}';", $"DELETE FROM dbo.T_PAIE_CLOTURE WHERE BDD_SAGE = N'MIGTEST_{suffixe}';" })
            (await FluentActions.Awaiting(() => ScalarAsync<int>(connection, interdit + " SELECT 1;")).Should().ThrowAsync<SqlException>())
                .Which.Number.Should().Be(51992, interdit);
        await FluentActions.Awaiting(() => ScalarAsync<int>(connection, insertion)).Should().ThrowAsync<SqlException>("une seule clôture par base et période");
        (await ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM dbo.T_PAIE_CLOTURE WHERE BDD_SAGE = N'MIGTEST_{suffixe}';")).Should().Be(1);
    }

    [MigrationIntegrationFact]
    public async Task ClosedPeriodWriteCheck_RefusesAnySourceWriteInAClosedPeriod_AndWritesNothing()
    {
        var connectionString = DisposableConnectionString();
        await Migrator(connectionString, lockTimeoutSeconds: 60).RunAsync();
        var options = new DbContextOptionsBuilder<SoftTime.Infrastructure.Persistence.SoftTimeDbContext>().UseSqlServer(connectionString).Options;
        var baseSage = "MIGTEST_W_" + Guid.NewGuid().ToString("N")[..8];
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var envoi = await ScalarAsync<int>(connection, $"""
                INSERT INTO dbo.T_PAIE_ENVOI (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, ENVOYE_LE, ENVOYE_PAR, STATUT, LIGNES, TYPE_BASE)
                VALUES (N'{baseSage}', '2026-08-17', '2026-08-23', SYSUTCDATETIME(), N'test', N'SUCCES', 1, N'STANDARD');
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """);
            var instantane = await ScalarAsync<int>(connection, $"""
                INSERT INTO dbo.T_PAIE_INSTANTANE (ENVOI_ID, BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, TYPE_BASE, ENVOYE_LE, AVEC_FILTRE, REMISE_A_ZERO,
                    HEURES_PAR_JOUR, VERSION_FORMAT, EMPREINTE, NB_LIGNES, NB_SANS_LIGNE, NB_NON_REMIS, CREE_LE)
                VALUES ({envoi}, N'{baseSage}', '2026-08-17', '2026-08-23', N'STANDARD', SYSUTCDATETIME(), 0, 1, 8, 1, REPLICATE('a', 64), 0, 0, 0, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """);
            await ScalarAsync<int>(connection, $"""
                INSERT INTO dbo.T_PAIE_CLOTURE (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN, CLOTURE_LE, CLOTURE_PAR, ENVOI_ID, INSTANTANE_ID, EMPREINTE, STATUT_CONTROLE, RESUME_CONTROLE)
                VALUES (N'{baseSage}', '2026-08-17', '2026-08-23', SYSUTCDATETIME(), N'test', {envoi}, {instantane}, REPLICATE('a', 64), N'Pret', N'r');
                SELECT 1;
                """);
        }

        async Task<int> Pointages()
        {
            await using var db = new SoftTime.Infrastructure.Persistence.SoftTimeDbContext(options);
            return await db.Set<SoftTime.Domain.Entities.SoftTime.Pointage>().CountAsync(p => p.NomBddSage == baseSage);
        }
        async Task Ecrire(Action<SoftTime.Infrastructure.Persistence.SoftTimeDbContext> action)
        {
            await using var db = new SoftTime.Infrastructure.Persistence.SoftTimeDbContext(options);
            action(db);
            await new SoftTime.Infrastructure.Repositories.UnitOfWork(db).SaveChangesAsync();
        }
        SoftTime.Domain.Entities.SoftTime.Pointage Pointage(DateTime jour) => new()
        {
            NomBddSage = baseSage, MatriculeSage = "M1", DatePointage = jour, HeurePointage = new TimeSpan(8, 0, 0), DateImportation = DateTime.Now
        };

        // Période voisine : accepté. Période clôturée : refusé, rien d'écrit (ajout, déplacement, suppression, planning).
        await Ecrire(db => db.Add(Pointage(new DateTime(2026, 8, 24))));
        (await Pointages()).Should().Be(1);
        await FluentActions.Awaiting(() => Ecrire(db => db.AddRange(Pointage(new DateTime(2026, 8, 25)), Pointage(new DateTime(2026, 8, 20)))))
            .Should().ThrowAsync<ConflictException>().WithMessage("Période clôturée (du 17/08/2026 au 23/08/2026) : modification impossible.*");
        (await Pointages()).Should().Be(1, "le lot entier est refusé");
        await FluentActions.Awaiting(() => Ecrire(db =>
        {
            var p = db.Set<SoftTime.Domain.Entities.SoftTime.Pointage>().Single(x => x.NomBddSage == baseSage);
            p.DatePointage = new DateTime(2026, 8, 23);
        })).Should().ThrowAsync<ConflictException>();
        await FluentActions.Awaiting(() => Ecrire(db => db.Add(new SoftTime.Domain.Entities.SoftTime.PlanningSalarie
        {
            IdSal = 1, DateP = new DateTime(2026, 8, 18), BddSage = baseSage, Ha = new DateTime(2026, 8, 18, 8, 0, 0), Hd = new DateTime(2026, 8, 18, 17, 0, 0)
        }))).Should().ThrowAsync<ConflictException>();
        await FluentActions.Awaiting(() => Ecrire(db => db.Add(new SoftTime.Domain.Entities.SoftTime.HsExoImp
        {
            BddSage = baseSage, Matricule = "M1", PeriodeDebut = new DateTime(2026, 8, 1), PeriodeFin = new DateTime(2026, 8, 31)
        }))).Should().ThrowAsync<ConflictException>("une période de calcul qui recoupe la période clôturée est refusée");
        // Une autre base reste modifiable sur les mêmes dates.
        var autre = Pointage(new DateTime(2026, 8, 20));
        autre.NomBddSage = baseSage + "_AUTRE";
        await Ecrire(db => db.Add(autre));
    }

    private sealed class SingleScopeFactory(SoftTime.Domain.Repositories.IUnitOfWork uow) : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => new Scope(uow);

        private sealed class Scope(SoftTime.Domain.Repositories.IUnitOfWork uow) : Microsoft.Extensions.DependencyInjection.IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(SoftTime.Domain.Repositories.IUnitOfWork) ? uow : null;
            public void Dispose() { }
        }
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
