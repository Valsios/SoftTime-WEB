using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

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
        var entity = new T_CODEABSENCE { INTITULE_ABSENCE = $"audit-{Guid.NewGuid():N}" };
        db.Add(entity);

        db.SaveChanges();

        var entries = db.suivi_JOURNAL.Where(item => item.TABLES == "T_CODEABSENCE").ToList();
        Assert.True(entity.ID_ABSENCE > 0);
        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.Equal($"ID_ABSENCE={entity.ID_ABSENCE}", entry.CLES));
        Assert.DoesNotContain(entries.Select(entry => entry.CLES), key => key!.Contains('-'));
        transaction.Rollback();
    }

    [Fact]
    public void Non_generated_key_is_preserved_in_the_journal()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        var code = $"AUDIT-{Guid.NewGuid():N}";
        db.Add(new T_FIELD_ROLE { Code = code, EntityKind = "EMPLOYEE", Label = "Audit", SystemType = "SAGE" });

        db.SaveChanges();

        var entries = db.suivi_JOURNAL.Where(item => item.TABLES == "T_FIELD_ROLE").ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.Equal($"Code={code}", entry.CLES));
        transaction.Rollback();
    }

    [Fact]
    public void Audit_write_failure_rolls_back_the_business_insert()
    {
        EnsureInitialized();
        var label = $"rollback-{Guid.NewGuid():N}";
        using (var db = CreateAuditedContext(new ThrowOnJournalInsertInterceptor()))
        {
            db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = label });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        using var verification = CreatePlainContext();
        Assert.False(verification.T_CODEABSENCE.Any(item => item.INTITULE_ABSENCE == label));
    }

    [Fact]
    public void Existing_transaction_is_reused_without_being_committed()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext();
        using var transaction = db.Database.BeginTransaction();
        db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = $"external-{Guid.NewGuid():N}" });

        db.SaveChanges();

        Assert.NotNull(db.Database.CurrentTransaction);
        Assert.True(transaction.GetDbTransaction().Connection is not null);
        transaction.Rollback();
    }

    [Fact]
    public void Disabled_audit_writes_no_journal_entry()
    {
        EnsureInitialized();
        using var db = CreateAuditedContext(options: new AuditJournalOptions { Enabled = false });
        using var transaction = db.Database.BeginTransaction();
        db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = $"disabled-{Guid.NewGuid():N}" });

        db.SaveChanges();

        Assert.Empty(db.suivi_JOURNAL);
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
            new AuditJournalSaveChangesInterceptor(new TestCurrentUser(), new TestCompanyContext(), options ?? new AuditJournalOptions()));
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
            var included = new HashSet<Type> { typeof(T_CODEABSENCE), typeof(T_FIELD_ROLE), typeof(suivi_JOURNAL) };
            foreach (var property in typeof(SoftTimeDbContext).GetProperties()
                         .Where(property => property.PropertyType.IsGenericType
                                            && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                                            && !included.Contains(property.PropertyType.GenericTypeArguments[0])))
                modelBuilder.Ignore(property.PropertyType.GenericTypeArguments[0]);

            modelBuilder.Entity<T_CODEABSENCE>(entity =>
            {
                entity.HasKey(item => item.ID_ABSENCE);
                entity.Property(item => item.ID_ABSENCE).ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<T_FIELD_ROLE>(entity =>
            {
                entity.HasKey(item => item.Code);
                entity.Property(item => item.Code).HasMaxLength(64);
                entity.Property(item => item.Label).HasMaxLength(128);
                entity.Property(item => item.SystemType).HasMaxLength(16);
                entity.Property(item => item.EntityKind).HasMaxLength(32);
            });
            modelBuilder.Entity<suivi_JOURNAL>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedOnAdd();
                entity.Property(item => item.DATE_UTC).HasColumnType("datetime2");
                entity.Property(item => item.TABLES).HasMaxLength(128);
                entity.Property(item => item.CLES).HasMaxLength(450);
                entity.Property(item => item.COLONNES).HasMaxLength(128);
                entity.Property(item => item.ACTIONS).HasMaxLength(32);
                entity.Property(item => item.USERS).HasMaxLength(256);
                entity.Property(item => item.BASE_SAGE).HasMaxLength(128);
                entity.Property(item => item.BASE_POINTEUSE).HasMaxLength(128);
                entity.Property(item => item.SOURCE).HasMaxLength(64);
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
