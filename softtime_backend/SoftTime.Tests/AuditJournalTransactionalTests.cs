using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

namespace SoftTime.Tests;

[Collection("Audit journal relational")]
public class AuditJournalTransactionalTests
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=SoftTimeAuditJournalTransactionalTestsV3;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Table visée par la panne simulée (<see cref="ThrowOnJournalInsertInterceptor"/>).</summary>
    private const string JournalTable = "suivi_JOURNAL";

    [Fact]
    public void Model_JournalAudit_IsMappedToTheTableTargetedBySimulatedFailure()
    {
        // Arrange
        using var db = CreatePlainContext();

        // Act
        var table = db.Model.FindEntityType(typeof(JournalAudit))!.GetTableName();

        // Assert : un renommage futur rendrait la panne simulée inopérante sans bruit ; il échoue ici, clairement.
        table.Should().Be(JournalTable, "la panne simulée ne vise que les commandes contenant [suivi_JOURNAL]");
        db.Model.FindEntityType(typeof(CodeAbsence))!.GetTableName().Should().Be("T_CODEABSENCE");
        db.Model.FindEntityType(typeof(FieldRole))!.GetTableName().Should().Be("T_FIELD_ROLE");
    }

    private static readonly Lazy<bool> DatabaseInitialized = new(EnsureDatabase);

    [Fact]
    public void SaveChanges_GeneratedIdentity_IsWrittenToJournalAfterBusinessInsert()
    {
        // Arrange
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        var entity = new CodeAbsence { IntituleAbsence = $"audit-{Guid.NewGuid():N}" };
        db.Add(entity);
        db.SaveChanges();

        // Act
        var entries = db.JournalAudits.Where(item => item.Tables == "T_CODEABSENCE").ToList();

        // Assert
        (entity.IdAbsence > 0).Should().BeTrue();
        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(entry => entry.Cles.Should().Be($"ID_ABSENCE={entity.IdAbsence}"));
        entries.Select(entry => entry.Cles).Should().NotContain(key => key!.Contains('-'));
        transaction.Rollback();
    }

    [Fact]
    public void SaveChanges_NonGeneratedKey_IsPreservedInJournal()
    {
        // Arrange
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        var code = $"AUDIT-{Guid.NewGuid():N}";
        db.Add(new FieldRole { Code = code, EntityKind = "EMPLOYEE", Label = "Audit", SystemType = "SAGE" });
        db.SaveChanges();

        // Act
        var entries = db.JournalAudits.Where(item => item.Tables == "T_FIELD_ROLE").ToList();

        // Assert
        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(entry => entry.Cles.Should().Be($"Code={code}"));
        transaction.Rollback();
    }

    [Fact]
    public void SaveChanges_AuditWriteFails_RollsBackBusinessInsert()
    {
        EnsureInitialized();
        var label = $"rollback-{Guid.NewGuid():N}";
        using (var db = CreateAuditedContext(new ThrowOnJournalInsertInterceptor()))
        {
            db.Model.FindEntityType(typeof(JournalAudit))!.GetTableName().Should().Be(JournalTable, "sinon la panne simulée ne se déclenche pas");
            db.Add(new CodeAbsence { IntituleAbsence = label });
            FluentActions.Invoking(() => db.SaveChanges()).Should().ThrowExactly<DbUpdateException>();
        }

        using var verification = CreatePlainContext();
        verification.CodeAbsences.Any(item => item.IntituleAbsence == label).Should().BeFalse();
        verification.JournalAudits.Any(item => item.Nouveaux == label).Should().BeFalse();
    }

    [Fact]
    public void SaveChanges_ExistingTransaction_IsReusedWithoutCommit()
    {
        // Arrange
        EnsureInitialized();
        using var sut = CreateAuditedContext();
        using var transaction = sut.Database.BeginTransaction();
        sut.Add(new CodeAbsence { IntituleAbsence = $"external-{Guid.NewGuid():N}" });

        // Act
        sut.SaveChanges();

        // Assert
        sut.Database.CurrentTransaction.Should().NotBeNull();
        (transaction.GetDbTransaction().Connection is not null).Should().BeTrue();
        transaction.Rollback();
    }

    [Fact]
    public void SaveChanges_AuditDisabled_WritesNoJournalEntry()
    {
        // Arrange
        EnsureInitialized();
        using var sut = CreateAuditedContext(options: new AuditJournalOptions { Enabled = false });
        using var transaction = sut.Database.BeginTransaction();
        var label = $"disabled-{Guid.NewGuid():N}";
        var entity = new CodeAbsence { IntituleAbsence = label };
        sut.Add(entity);

        // Act
        sut.SaveChanges();

        // Assert : aucune ligne de journal pour la ligne de CE test (libellé et clé uniques), quel que soit le contenu
        // laissé par d'autres passages ; la ligne métier, elle, est bien écrite.
        sut.CodeAbsences.Any(item => item.IntituleAbsence == label).Should().BeTrue();
        sut.JournalAudits.Where(item => item.Nouveaux == label || item.Cles == $"ID_ABSENCE={entity.IdAbsence}")
            .Should().BeEmpty();
        transaction.Rollback();
    }

    private static void EnsureInitialized() => _ = DatabaseInitialized.Value;

    private static bool EnsureDatabase()
    {
        using var db = CreatePlainContext();
        return db.Database.EnsureCreated();
    }

    private static SoftTimeDbContext CreatePlainContext()
        => new AuditTestDbContext(CreateOptions().Options);

    private static SoftTimeDbContext CreateAuditedContext(
        DbCommandInterceptor? commandInterceptor = null,
        AuditJournalOptions? options = null)
    {
        var builder = CreateOptions();
        if (commandInterceptor is not null)
            builder.AddInterceptors(commandInterceptor);
        return new AuditTestDbContext(
            builder.Options,
            new AuditJournalSaveChangesInterceptor(new TestCurrentUser(), new TestCompanyContext(), Options.Create(options ?? new AuditJournalOptions())));
    }

    private static DbContextOptionsBuilder<SoftTimeDbContext> CreateOptions()
        => new DbContextOptionsBuilder<SoftTimeDbContext>().UseSqlServer(ConnectionString);

    private sealed class AuditTestDbContext : SoftTimeDbContext
    {
        public AuditTestDbContext(DbContextOptions<SoftTimeDbContext> options) : base(options) { }

        public AuditTestDbContext(
            DbContextOptions<SoftTimeDbContext> options,
            AuditJournalSaveChangesInterceptor auditJournal) : base(options, auditJournal) { }

        /// <summary>
        /// Seules les trois entités du test sont gardées, avec leurs configurations EF RÉELLES (tables T_CODEABSENCE,
        /// T_FIELD_ROLE, suivi_JOURNAL et leurs colonnes) : depuis le passage des entités en PascalCase, le nom de table
        /// ne vient plus que de ces configurations, qu'un mapping écrit à la main ne reproduirait pas.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var included = new HashSet<Type> { typeof(CodeAbsence), typeof(FieldRole), typeof(JournalAudit) };
            foreach (var property in typeof(SoftTimeDbContext).GetProperties()
                         .Where(property => property.PropertyType.IsGenericType
                                            && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                                            && !included.Contains(property.PropertyType.GenericTypeArguments[0])))
                modelBuilder.Ignore(property.PropertyType.GenericTypeArguments[0]);

            modelBuilder.ApplyConfiguration(new CodeAbsenceConfiguration());
            modelBuilder.ApplyConfiguration(new FieldRoleConfiguration());
            modelBuilder.ApplyConfiguration(new JournalAuditConfiguration());
        }
    }

    private sealed class ThrowOnJournalInsertInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.Contains("[suivi_JOURNAL]", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Journal write failure for test.");
            return result;
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public decimal? UserId => 1;
        public string? Login => "audit-test";
        public decimal? RoleId => 1;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => true;
    }

    private sealed class TestCompanyContext : ICompanyContext
    {
        public string? SageDatabase => "SAGE_TEST";
        public string? PointeuseDatabase => null;
        public void Set(string sage, string? pointeuse) { }
    }
}
