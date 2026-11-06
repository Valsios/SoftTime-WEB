using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        db.Add(new CodeAbsence { IntituleAbsence = "Congé", NotPay = false });

        Capture(db);

        Assert.Contains(AuditEntries(db), e => e.Actions == "Insertion" && e.Colonnes == "INTITULE_ABSENCE");
    }

    [Fact]
    public void Modification_of_two_columns_creates_two_entries_with_one_correlation()
    {
        using var db = CreateContext();
        var entity = new CodeAbsence { IdAbsence = 10, IntituleAbsence = "Avant", NotPay = false };
        db.Attach(entity);
        entity.IntituleAbsence = "Après";
        entity.NotPay = true;
        db.Entry(entity).Property(e => e.IntituleAbsence).IsModified = true;
        db.Entry(entity).Property(e => e.NotPay).IsModified = true;

        Capture(db);

        var entries = AuditEntries(db).Where(e => e.Tables == "T_CODEABSENCE").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Single(entries.Select(e => e.Correlations).Distinct());
        var title = Assert.Single(entries.Where(entry => entry.Colonnes == "INTITULE_ABSENCE"));
        Assert.Equal("Avant", title.Anciens);
        Assert.Equal("Après", title.Nouveaux);
        var paid = Assert.Single(entries.Where(entry => entry.Colonnes == "NOT_PAY"));
        Assert.Equal("False", paid.Anciens);
        Assert.Equal("True", paid.Nouveaux);
    }

    [Fact]
    public void Modification_of_one_column_creates_one_entry()
    {
        using var db = CreateContext();
        var entity = new JourFerie { Id = 1, Intitule = "Avant", BddSage = "SFOI_PAIE2025" };
        db.Attach(entity);
        entity.Intitule = "Après";
        db.Entry(entity).State = EntityState.Modified;

        Capture(db);

        var entry = Assert.Single(AuditEntries(db));
        Assert.Equal("INTITULE", entry.Colonnes);
        Assert.Equal("Avant", entry.Anciens);
        Assert.Equal("Après", entry.Nouveaux);
    }

    [Fact]
    public void Fully_marked_entity_with_no_value_change_creates_no_entry()
    {
        using var db = CreateContext();
        var entity = new JourFerie { Id = 1, Intitule = "Identique", BddSage = "SFOI_PAIE2025" };
        db.Attach(entity);
        db.Entry(entity).State = EntityState.Modified;

        Capture(db);

        Assert.Empty(AuditEntries(db));
    }

    [Fact]
    public void Fully_marked_entity_with_one_value_change_creates_one_entry()
    {
        using var db = CreateContext();
        var entity = new JourFerie { Id = 1, Intitule = "Avant", BddSage = "SFOI_PAIE2025" };
        db.Attach(entity);
        entity.Intitule = "Après";
        db.Entry(entity).State = EntityState.Modified;

        Capture(db);

        var entry = Assert.Single(AuditEntries(db));
        Assert.Equal("INTITULE", entry.Colonnes);
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
        var entity = new JourFerie { Id = 1, Date = original };
        db.Attach(entity);
        entity.Date = current;
        db.Entry(entity).Property(e => e.Date).IsModified = true;

        Capture(db);

        var entry = Assert.Single(AuditEntries(db));
        Assert.Equal("2030-01-01 00:00:00.0000000", entry.Anciens);
        Assert.Equal("2030-01-02 00:00:00.0000000", entry.Nouveaux);
        Assert.DoesNotContain("Z", entry.Anciens);
        Assert.DoesNotContain("Z", entry.Nouveaux);
    }

    [Fact]
    public void Suppression_creates_delete_entries()
    {
        using var db = CreateContext();
        var entity = new CodeAbsence { IdAbsence = 10, IntituleAbsence = "Congé" };
        db.Attach(entity);
        db.Remove(entity);

        Capture(db);

        Assert.All(AuditEntries(db), entry => Assert.Equal("Suppression", entry.Actions));
    }

    [Fact]
    public void Rollback_keeps_journal_in_the_same_pending_unit_of_work()
    {
        using var db = CreateContext();
        db.Add(new CodeAbsence { IntituleAbsence = "Congé" });

        Capture(db);

        Assert.Equal(EntityState.Added, db.Entry(db.ChangeTracker.Entries<CodeAbsence>().Single().Entity).State);
        Assert.All(AuditEntries(db), entry => Assert.Equal(EntityState.Added, db.Entry(entry).State));
    }

    [Fact]
    public void Sensitive_columns_are_masked()
    {
        using var db = CreateContext();
        db.Add(new Responsable { Login = "agent", Password = "secret" });

        Capture(db);

        var entry = Assert.Single(AuditEntries(db).Where(e => e.Colonnes == "PASSWORD"));
        Assert.Equal("********", entry.Anciens);
        Assert.Equal("********", entry.Nouveaux);
    }

    [Theory]
    [InlineData(typeof(SuiviModification))]
    [InlineData(typeof(SuiviTable))]
    [InlineData(typeof(SuiviConfiguration))]
    [InlineData(typeof(JournalAudit))]
    [InlineData(typeof(HistoriquePointage))]
    [InlineData(typeof(HistoriqueAnomalie))]
    [InlineData(typeof(HistoriqueHs))]
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
        db.Add(new CodeAbsence { IntituleAbsence = "Congé" });

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
            db.Add(new CodeAbsence { IntituleAbsence = $"Congé {index}" });

        Capture(db, options: new AuditJournalOptions { DetailThreshold = 2 });

        var entry = Assert.Single(AuditEntries(db));
        Assert.Null(entry.Colonnes);
        Assert.Contains("Insertion=3", entry.Nouveaux);
    }

    [Fact]
    public void Missing_user_uses_system_identity()
    {
        using var db = CreateContext();
        db.Add(new CodeAbsence { IntituleAbsence = "Congé" });

        Capture(db, login: null);

        Assert.All(AuditEntries(db), entry => Assert.Equal("SYSTEME:SoftTimeDbContext", entry.Users));
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
        => new(new TestCurrentUser(login), new TestCompanyContext(), Options.Create(options ?? new AuditJournalOptions()));

    private static List<JournalAudit> AuditEntries(SoftTimeDbContext db)
        => db.ChangeTracker.Entries<JournalAudit>()
            .Select(entry => entry.Entity)
            .Where(entry => entry.Source == "EF:SoftTimeDbContext")
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
