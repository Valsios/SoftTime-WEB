using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

public class AuditJournalSaveChangesInterceptorTests
{
    [Fact]
    public void Insertion_creates_detail_entries()
    {
        using var db = CreateContext();
        db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = "Congé", NOT_PAY = false });

        Capture(db);

        Assert.Contains(AuditEntries(db), e => e.ACTIONS == "Insertion" && e.COLONNES == "INTITULE_ABSENCE");
    }

    [Fact]
    public void Modification_of_two_columns_creates_two_entries_with_one_correlation()
    {
        using var db = CreateContext();
        var entity = new T_CODEABSENCE { ID_ABSENCE = 10, INTITULE_ABSENCE = "Avant", NOT_PAY = false };
        db.Attach(entity);
        entity.INTITULE_ABSENCE = "Après";
        entity.NOT_PAY = true;
        db.Entry(entity).Property(e => e.INTITULE_ABSENCE).IsModified = true;
        db.Entry(entity).Property(e => e.NOT_PAY).IsModified = true;

        Capture(db);

        var entries = AuditEntries(db).Where(e => e.TABLES == "T_CODEABSENCE").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Single(entries.Select(e => e.CORRELATIONS).Distinct());
        var title = Assert.Single(entries.Where(entry => entry.COLONNES == "INTITULE_ABSENCE"));
        Assert.Equal("Avant", title.ANCIENS);
        Assert.Equal("Après", title.NOUVEAUX);
        var paid = Assert.Single(entries.Where(entry => entry.COLONNES == "NOT_PAY"));
        Assert.Equal("False", paid.ANCIENS);
        Assert.Equal("True", paid.NOUVEAUX);
    }

    [Fact]
    public void Modification_of_one_column_creates_one_entry()
    {
        using var db = CreateContext();
        var entity = new T_FERIE { ID = 1, INTITULE = "Avant", BDD_SAGE = "SFOI_PAIE2025" };
        db.Attach(entity);
        entity.INTITULE = "Après";
        db.Entry(entity).State = EntityState.Modified;

        Capture(db);

        var entry = Assert.Single(AuditEntries(db));
        Assert.Equal("INTITULE", entry.COLONNES);
        Assert.Equal("Avant", entry.ANCIENS);
        Assert.Equal("Après", entry.NOUVEAUX);
    }

    [Fact]
    public void Fully_marked_entity_with_no_value_change_creates_no_entry()
    {
        using var db = CreateContext();
        var entity = new T_FERIE { ID = 1, INTITULE = "Identique", BDD_SAGE = "SFOI_PAIE2025" };
        db.Attach(entity);
        db.Entry(entity).State = EntityState.Modified;

        Capture(db);

        Assert.Empty(AuditEntries(db));
    }

    [Fact]
    public void Fully_marked_entity_with_one_value_change_creates_one_entry()
    {
        using var db = CreateContext();
        var entity = new T_FERIE { ID = 1, INTITULE = "Avant", BDD_SAGE = "SFOI_PAIE2025" };
        db.Attach(entity);
        entity.INTITULE = "Après";
        db.Entry(entity).State = EntityState.Modified;

        Capture(db);

        var entry = Assert.Single(AuditEntries(db));
        Assert.Equal("INTITULE", entry.COLONNES);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void Date_values_preserve_stored_clock_time_without_utc_conversion(DateTimeKind kind)
    {
        using var db = CreateContext();
        var original = new DateTime(2030, 1, 1, 0, 0, 0, kind);
        var current = new DateTime(2030, 1, 2, 0, 0, 0, kind);
        var entity = new T_FERIE { ID = 1, DATE = original };
        db.Attach(entity);
        entity.DATE = current;
        db.Entry(entity).Property(e => e.DATE).IsModified = true;

        Capture(db);

        var entry = Assert.Single(AuditEntries(db));
        Assert.Equal("2030-01-01 00:00:00.0000000", entry.ANCIENS);
        Assert.Equal("2030-01-02 00:00:00.0000000", entry.NOUVEAUX);
        Assert.DoesNotContain("Z", entry.ANCIENS);
        Assert.DoesNotContain("Z", entry.NOUVEAUX);
    }

    [Fact]
    public void Suppression_creates_delete_entries()
    {
        using var db = CreateContext();
        var entity = new T_CODEABSENCE { ID_ABSENCE = 10, INTITULE_ABSENCE = "Congé" };
        db.Attach(entity);
        db.Remove(entity);

        Capture(db);

        Assert.All(AuditEntries(db), entry => Assert.Equal("Suppression", entry.ACTIONS));
    }

    [Fact]
    public void Rollback_keeps_journal_in_the_same_pending_unit_of_work()
    {
        using var db = CreateContext();
        db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = "Congé" });

        Capture(db);

        Assert.Equal(EntityState.Added, db.Entry(db.ChangeTracker.Entries<T_CODEABSENCE>().Single().Entity).State);
        Assert.All(AuditEntries(db), entry => Assert.Equal(EntityState.Added, db.Entry(entry).State));
    }

    [Fact]
    public void Sensitive_columns_are_masked()
    {
        using var db = CreateContext();
        db.Add(new T_RESPONSABLE { LOGIN = "agent", PASSWORD = "secret" });

        Capture(db);

        var entry = Assert.Single(AuditEntries(db).Where(e => e.COLONNES == "PASSWORD"));
        Assert.Equal("********", entry.ANCIENS);
        Assert.Equal("********", entry.NOUVEAUX);
    }

    [Theory]
    [InlineData(typeof(suivi_SUIVI))]
    [InlineData(typeof(suivi_T))]
    [InlineData(typeof(suivi_TBLCLN))]
    [InlineData(typeof(suivi_JOURNAL))]
    [InlineData(typeof(T_HISTORIQUE_POINTAGE))]
    [InlineData(typeof(T_HISTORIQUE_ANOMALIE))]
    [InlineData(typeof(T_HISTORIQUE_HS))]
    public void Technical_and_history_tables_are_excluded(Type type)
    {
        using var db = CreateContext();
        db.Add(Activator.CreateInstance(type)!);

        Capture(db);

        Assert.Empty(AuditEntries(db));
    }

    [Fact]
    public void Capturing_twice_does_not_create_recursive_or_duplicate_entries()
    {
        using var db = CreateContext();
        db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = "Congé" });

        var interceptor = CreateInterceptor();
        interceptor.CaptureChanges(db);
        interceptor.CaptureChanges(db);

        Assert.Equal(2, AuditEntries(db).Count);
    }

    [Fact]
    public void Over_threshold_creates_one_summary()
    {
        using var db = CreateContext();
        for (var index = 0; index < 3; index++)
            db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = $"Congé {index}" });

        Capture(db, options: new AuditJournalOptions { DetailThreshold = 2 });

        var entry = Assert.Single(AuditEntries(db));
        Assert.Null(entry.COLONNES);
        Assert.Contains("Insertion=3", entry.NOUVEAUX);
    }

    [Fact]
    public void Missing_user_uses_system_identity()
    {
        using var db = CreateContext();
        db.Add(new T_CODEABSENCE { INTITULE_ABSENCE = "Congé" });

        Capture(db, login: null);

        Assert.All(AuditEntries(db), entry => Assert.Equal("SYSTEME:SoftTimeDbContext", entry.USERS));
    }

    private static SoftTimeDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimeAuditJournalTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    private static void Capture(SoftTimeDbContext db, string? login = "agent", AuditJournalOptions? options = null)
        => CreateInterceptor(login, options).CaptureChanges(db);

    private static AuditJournalSaveChangesInterceptor CreateInterceptor(
        string? login = "agent",
        AuditJournalOptions? options = null)
        => new(new TestCurrentUser(login), new TestCompanyContext(), options ?? new AuditJournalOptions());

    private static List<suivi_JOURNAL> AuditEntries(SoftTimeDbContext db)
        => db.ChangeTracker.Entries<suivi_JOURNAL>()
            .Select(entry => entry.Entity)
            .Where(entry => entry.SOURCE == "EF:SoftTimeDbContext")
            .ToList();

    private sealed class TestCurrentUser(string? login) : ICurrentUser
    {
        public decimal? UserId => 1;
        public string? Login => login;
        public decimal? RoleId => 1;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => true;
    }

    private sealed class TestCompanyContext : ICompanyContext
    {
        public string? SageDatabase => "SAGE_TEST";
        public string? PointeuseDatabase => "PTE_TEST";
        public void Set(string sage, string? pointeuse) { }
    }
}

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

[CollectionDefinition("Audit journal relational", DisableParallelization = true)]
public sealed class AuditJournalRelationalCollection;
