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
        // Arrange
        using var db = CreateContext();
        db.Add(new CodeAbsence { IntituleAbsence = "Congé", NotPay = false });

        // Act
        Capture(db);

        // Assert
        AuditEntries(db).Should().Contain(e => e.Actions == "Insertion" && e.Colonnes == "INTITULE_ABSENCE");
    }

    [Fact]
    public void Modification_of_two_columns_creates_two_entries_with_one_correlation()
    {
        // Arrange
        using var db = CreateContext();
        var entity = new CodeAbsence { IdAbsence = 10, IntituleAbsence = "Avant", NotPay = false };
        db.Attach(entity);
        entity.IntituleAbsence = "Après";
        entity.NotPay = true;
        db.Entry(entity).Property(e => e.IntituleAbsence).IsModified = true;
        db.Entry(entity).Property(e => e.NotPay).IsModified = true;
        Capture(db);

        // Act
        var entries = AuditEntries(db).Where(e => e.Tables == "T_CODEABSENCE").ToList();

        // Assert
        entries.Count.Should().Be(2);
        entries.Select(e => e.Correlations).Distinct().Should().ContainSingle();
        var title = entries.Where(entry => entry.Colonnes == "INTITULE_ABSENCE").Should().ContainSingle().Which;
        title.Anciens.Should().Be("Avant");
        title.Nouveaux.Should().Be("Après");
        var paid = entries.Where(entry => entry.Colonnes == "NOT_PAY").Should().ContainSingle().Which;
        paid.Anciens.Should().Be("False");
        paid.Nouveaux.Should().Be("True");
    }

    [Fact]
    public void Modification_of_one_column_creates_one_entry()
    {
        // Arrange
        using var db = CreateContext();
        var entity = new JourFerie { Id = 1, Intitule = "Avant", BddSage = "SFOI_PAIE2025" };
        db.Attach(entity);
        entity.Intitule = "Après";
        db.Entry(entity).State = EntityState.Modified;

        // Act
        Capture(db);

        // Assert
        var entry = AuditEntries(db).Should().ContainSingle().Which;
        entry.Colonnes.Should().Be("INTITULE");
        entry.Anciens.Should().Be("Avant");
        entry.Nouveaux.Should().Be("Après");
    }

    [Fact]
    public void Fully_marked_entity_with_no_value_change_creates_no_entry()
    {
        // Arrange
        using var db = CreateContext();
        var entity = new JourFerie { Id = 1, Intitule = "Identique", BddSage = "SFOI_PAIE2025" };
        db.Attach(entity);
        db.Entry(entity).State = EntityState.Modified;

        // Act
        Capture(db);

        // Assert
        AuditEntries(db).Should().BeEmpty();
    }

    [Fact]
    public void Fully_marked_entity_with_one_value_change_creates_one_entry()
    {
        // Arrange
        using var db = CreateContext();
        var entity = new JourFerie { Id = 1, Intitule = "Avant", BddSage = "SFOI_PAIE2025" };
        db.Attach(entity);
        entity.Intitule = "Après";
        db.Entry(entity).State = EntityState.Modified;

        // Act
        Capture(db);

        // Assert
        var entry = AuditEntries(db).Should().ContainSingle().Which;
        entry.Colonnes.Should().Be("INTITULE");
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void Date_values_preserve_stored_clock_time_without_utc_conversion(DateTimeKind kind)
    {
        // Arrange
        using var db = CreateContext();
        var original = new DateTime(2030, 1, 1, 0, 0, 0, kind);
        var current = new DateTime(2030, 1, 2, 0, 0, 0, kind);
        var entity = new JourFerie { Id = 1, Date = original };
        db.Attach(entity);
        entity.Date = current;
        db.Entry(entity).Property(e => e.Date).IsModified = true;

        // Act
        Capture(db);

        // Assert
        var entry = AuditEntries(db).Should().ContainSingle().Which;
        entry.Anciens.Should().Be("2030-01-01 00:00:00.0000000");
        entry.Nouveaux.Should().Be("2030-01-02 00:00:00.0000000");
        entry.Anciens.Should().NotContain("Z");
        entry.Nouveaux.Should().NotContain("Z");
    }

    [Fact]
    public void Suppression_creates_delete_entries()
    {
        // Arrange
        using var db = CreateContext();
        var entity = new CodeAbsence { IdAbsence = 10, IntituleAbsence = "Congé" };
        db.Attach(entity);
        db.Remove(entity);

        // Act
        Capture(db);

        // Assert
        AuditEntries(db).Should().AllSatisfy(entry => entry.Actions.Should().Be("Suppression"));
    }

    [Fact]
    public void Rollback_keeps_journal_in_the_same_pending_unit_of_work()
    {
        // Arrange
        using var db = CreateContext();
        db.Add(new CodeAbsence { IntituleAbsence = "Congé" });

        // Act
        Capture(db);

        // Assert
        db.Entry(db.ChangeTracker.Entries<CodeAbsence>().Single().Entity).State.Should().Be(EntityState.Added);
        AuditEntries(db).Should().AllSatisfy(entry => db.Entry(entry).State.Should().Be(EntityState.Added));
    }

    [Fact]
    public void Sensitive_columns_are_masked()
    {
        // Arrange
        using var db = CreateContext();
        db.Add(new Responsable { Login = "agent", Password = "secret" });

        // Act
        Capture(db);

        // Assert
        var entry = AuditEntries(db).Where(e => e.Colonnes == "PASSWORD").Should().ContainSingle().Which;
        entry.Anciens.Should().Be("********");
        entry.Nouveaux.Should().Be("********");
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
        // Arrange
        using var db = CreateContext();
        db.Add(Activator.CreateInstance(type)!);

        // Act
        Capture(db);

        // Assert
        AuditEntries(db).Should().BeEmpty();
    }

    [Fact]
    public void Capturing_twice_does_not_create_recursive_or_duplicate_entries()
    {
        // Arrange
        using var db = CreateContext();
        db.Add(new CodeAbsence { IntituleAbsence = "Congé" });
        var sut = CreateInterceptor();
        sut.CaptureChanges(db);

        // Act
        sut.CaptureChanges(db);

        // Assert
        AuditEntries(db).Count.Should().Be(2);
    }

    [Fact]
    public void Over_threshold_creates_one_summary()
    {
        // Arrange
        using var db = CreateContext();
        for (var index = 0; index < 3; index++)
            db.Add(new CodeAbsence { IntituleAbsence = $"Congé {index}" });

        // Act
        Capture(db, options: new AuditJournalOptions { DetailThreshold = 2 });

        // Assert
        var entry = AuditEntries(db).Should().ContainSingle().Which;
        entry.Colonnes.Should().BeNull();
        entry.Nouveaux.Should().Contain("Insertion=3");
    }

    [Fact]
    public void Missing_user_uses_system_identity()
    {
        // Arrange
        using var db = CreateContext();
        db.Add(new CodeAbsence { IntituleAbsence = "Congé" });

        // Act
        Capture(db, login: null);

        // Assert
        AuditEntries(db).Should().AllSatisfy(entry => entry.Users.Should().Be("SYSTEME:SoftTimeDbContext"));
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
