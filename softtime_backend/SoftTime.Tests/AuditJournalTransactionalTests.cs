using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

[Collection("Audit journal relational")]
public class AuditJournalTransactionalTests
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=SoftTimeAuditJournalTransactionalTestsV2;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly Lazy<bool> DatabaseInitialized = new(EnsureDatabase);

    [Fact]
    public void Generated_identity_is_written_to_the_journal_after_the_business_insert()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        var entity = new CodeAbsence { IntituleAbsence = $"audit-{Guid.NewGuid():N}" };
        db.Add(entity);

        db.SaveChanges();

        var entries = db.JournalAudits.Where(item => item.Tables == "T_CODEABSENCE").ToList();
        (entity.IdAbsence > 0).Should().BeTrue();
        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(entry => entry.Cles.Should().Be($"ID_ABSENCE={entity.IdAbsence}"));
        entries.Select(entry => entry.Cles).Should().NotContain(key => key!.Contains('-'));
        transaction.Rollback();
    }

    [Fact]
    public void Non_generated_key_is_preserved_in_the_journal()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        var code = $"AUDIT-{Guid.NewGuid():N}";
        db.Add(new FieldRole { Code = code, EntityKind = "EMPLOYEE", Label = "Audit", SystemType = "SAGE" });

        db.SaveChanges();

        var entries = db.JournalAudits.Where(item => item.Tables == "T_FIELD_ROLE").ToList();
        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(entry => entry.Cles.Should().Be($"Code={code}"));
        transaction.Rollback();
    }

    [Fact]
    public void Audit_write_failure_rolls_back_the_business_insert()
    {
        EnsureInitialized();
        var label = $"rollback-{Guid.NewGuid():N}";
        using (var db = CreateAuditedContext(new ThrowOnJournalInsertInterceptor()))
        {
            db.Add(new CodeAbsence { IntituleAbsence = label });
            FluentActions.Invoking(() => db.SaveChanges()).Should().ThrowExactly<DbUpdateException>();
        }

        using var verification = CreatePlainContext();
        verification.CodeAbsences.Any(item => item.IntituleAbsence == label).Should().BeFalse();
    }

    [Fact]
    public void Existing_transaction_is_reused_without_being_committed()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        db.Add(new CodeAbsence { IntituleAbsence = $"external-{Guid.NewGuid():N}" });

        db.SaveChanges();

        db.Database.CurrentTransaction.Should().NotBeNull();
        (transaction.GetDbTransaction().Connection is not null).Should().BeTrue();
        transaction.Rollback();
    }

    [Fact]
    public void Disabled_audit_writes_no_journal_entry()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext(options: new AuditJournalOptions { Enabled = false });
        using var transaction = db.Database.BeginTransaction();
        db.Add(new CodeAbsence { IntituleAbsence = $"disabled-{Guid.NewGuid():N}" });

        db.SaveChanges();

        db.JournalAudits.Should().BeEmpty();
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var included = new HashSet<Type> { typeof(CodeAbsence), typeof(FieldRole), typeof(JournalAudit) };
            foreach (var property in typeof(SoftTimeDbContext).GetProperties()
                         .Where(property => property.PropertyType.IsGenericType
                                            && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                                            && !included.Contains(property.PropertyType.GenericTypeArguments[0])))
                modelBuilder.Ignore(property.PropertyType.GenericTypeArguments[0]);

            modelBuilder.Entity<CodeAbsence>(entity =>
            {
                entity.HasKey(item => item.IdAbsence);
                entity.Property(item => item.IdAbsence).ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<FieldRole>(entity =>
            {
                entity.HasKey(item => item.Code);
                entity.Property(item => item.Code).HasMaxLength(64);
                entity.Property(item => item.Label).HasMaxLength(128);
                entity.Property(item => item.SystemType).HasMaxLength(16);
                entity.Property(item => item.EntityKind).HasMaxLength(32);
            });
            modelBuilder.Entity<JournalAudit>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedOnAdd();
                entity.Property(item => item.DateUtc).HasColumnType("datetime2");
                entity.Property(item => item.Tables).HasMaxLength(128);
                entity.Property(item => item.Cles).HasMaxLength(450);
                entity.Property(item => item.Colonnes).HasMaxLength(128);
                entity.Property(item => item.Actions).HasMaxLength(32);
                entity.Property(item => item.Users).HasMaxLength(256);
                entity.Property(item => item.BaseSage).HasMaxLength(128);
                entity.Property(item => item.BasePointeuse).HasMaxLength(128);
                entity.Property(item => item.Source).HasMaxLength(64);
            });
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
