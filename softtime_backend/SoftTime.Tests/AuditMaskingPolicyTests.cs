using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

/// <summary>
/// Liste de masquage du journal central : explicite par table, motifs génériques, et garde-fou par réflexion
/// sur toutes les colonnes de <see cref="SoftTimeDbContext"/>.
/// </summary>
public class AuditMaskingPolicyTests
{
    /// <summary>Motifs qui évoquent un secret dans un nom de colonne (plus larges que la liste générique).</summary>
    private static readonly string[] SecretLikeFragments =
        ["PASS", "PWD", "MDP", "SECRET", "TOKEN", "JETON", "API_KEY", "APIKEY", "PRIVATE", "HASH", "SALT", "CONNECTION", "CONNEXION"];

    [Theory]
    [InlineData("T_BDD_SAGE", "TMDP")]
    [InlineData("T_BDD_SAGE", "TLOGIN")]
    [InlineData("T_BDD_SAGE", "SERVEUR")]
    [InlineData("T_BDD_POINTEUSE", "TMDP")]
    [InlineData("T_BDD_POINTEUSE", "TLOGIN")]
    [InlineData("T_BDD_POINTEUSE", "SERVEUR")]
    [InlineData("T_RESPONSABLE", "PASSWORD")]
    [InlineData("t_bdd_sage", "tmdp")]
    [InlineData("T_QUELCONQUE", "API_KEY")]
    [InlineData("T_QUELCONQUE", "REFRESH_TOKEN")]
    [InlineData("T_QUELCONQUE", "CLIENT_SECRET")]
    [InlineData("T_QUELCONQUE", "CONNECTIONSTRING")]
    [InlineData("T_QUELCONQUE", "PRIVATE_KEY")]
    public void IsSensitive_ListedOrPatternColumn_ReturnsTrue(string table, string column)
    {
        // Act
        var result = AuditMaskingPolicy.IsSensitive(table, column);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("T_RESPONSABLE", "EMAIL")]
    [InlineData("T_RESPONSABLE", "MATRICULE")]
    [InlineData("T_RESPONSABLE", "LOGIN")]
    [InlineData("T_BDD_SAGE", "NOM_BD")]
    [InlineData("T_FERIE", "SERVEUR")]
    [InlineData("T_FERIE", null)]
    public void IsSensitive_EmailMatriculeOrOrdinaryColumn_ReturnsFalse(string table, string? column)
    {
        // Act
        var result = AuditMaskingPolicy.IsSensitive(table, column);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void MaskValue_NullOrSensitiveValue_KeepsNullAndMasksOnlySensitive()
    {
        // Act & Assert
        AuditMaskingPolicy.MaskValue("T_BDD_SAGE", "TMDP", null).Should().BeNull();
        AuditMaskingPolicy.MaskValue("T_BDD_SAGE", "TMDP", "clair").Should().Be(AuditMaskingPolicy.Mask);
        AuditMaskingPolicy.MaskValue("T_RESPONSABLE", "EMAIL", "a@b.test").Should().Be("a@b.test");
    }

    [Fact]
    public void MaskKey_MixedKeyColumns_MasksOnlySensitiveOnes()
    {
        // Act & Assert
        AuditMaskingPolicy.MaskKey("T_BDD_SAGE", "ID=3;TMDP=clair").Should().Be("ID=3;TMDP=********");
        AuditMaskingPolicy.MaskKey("T_RESPONSABLE", "IDRESPONSABLE=14").Should().Be("IDRESPONSABLE=14");
        AuditMaskingPolicy.MaskKey("T_RESPONSABLE", null).Should().BeNull();
    }

    [Fact]
    public void IsSensitive_EverySecretLikeColumnOfTheContext_ReturnsTrue()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var columns = db.Model.GetEntityTypes()
            .Select(type => (Table: type.GetTableName() ?? type.ClrType.Name, Type: type))
            .SelectMany(x => x.Type.GetProperties().Select(p => (x.Table, Column: p.GetColumnName() ?? p.Name, Property: p.Name)))
            .ToList();

        // Assert
        columns.Should().NotBeEmpty();
        var unmasked = columns
            .Where(c => SecretLikeFragments.Any(f => c.Column.Contains(f, StringComparison.OrdinalIgnoreCase)
                                                  || c.Property.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !AuditMaskingPolicy.IsSensitive(c.Table, c.Column))
            .Select(c => $"{c.Table}.{c.Column}")
            .ToList();
        unmasked.Should().BeEmpty();
        // Les identifiants de connexion des bases sont bien présents dans le modèle et couverts par la liste explicite.
        columns.Should().Contain(c => c.Table == "T_BDD_SAGE" && c.Column == "TMDP");
        columns.Should().Contain(c => c.Table == "T_BDD_POINTEUSE" && c.Column == "SERVEUR");
    }

    [Fact]
    public void CaptureChanges_DatabaseCredentials_MasksValueButKeepsColumnName()
    {
        // Arrange
        using var db = CreateContext();
        var entity = new BddSage { Id = 3, NomBd = "BASE", Serveur = "srv-avant", TLogin = "l-avant", TMdp = "m-avant" };
        db.Attach(entity);
        entity.Serveur = "srv-apres";
        entity.TLogin = "l-apres";
        entity.TMdp = "m-apres";
        Capture(db);
        var entries = AuditEntries(db);

        // Act
        var result = entries.Select(e => e.Colonnes).OrderBy(c => c).ToArray();

        // Assert
        result.Should().Equal(new[] { "SERVEUR", "TLOGIN", "TMDP" });
        entries.Should().AllSatisfy(e =>
        {
            e.Anciens.Should().Be(AuditMaskingPolicy.Mask);
            e.Nouveaux.Should().Be(AuditMaskingPolicy.Mask);
        });
    }

    [Fact]
    public void CaptureChanges_PointeusePassword_MasksPasswordAndKeepsEmail()
    {
        // Arrange
        using var db = CreateContext();
        db.Add(new BddPointeuse { Id = 9, NomBd = "PTG", TMdp = "clair" });
        db.Add(new Responsable { Login = "agent", Email = "agent@exemple.test", Password = "empreinte" });
        Capture(db);

        // Act
        var entries = AuditEntries(db);

        // Assert
        entries.Should().ContainSingle(e => e.Tables == "T_BDD_POINTEUSE" && e.Colonnes == "TMDP").Which.Nouveaux.Should().Be(AuditMaskingPolicy.Mask);
        entries.Should().ContainSingle(e => e.Colonnes == "PASSWORD").Which.Nouveaux.Should().Be(AuditMaskingPolicy.Mask);
        entries.Should().ContainSingle(e => e.Colonnes == "EMAIL").Which.Nouveaux.Should().Be("agent@exemple.test");
        entries.Should().NotContain(e => e.Anciens == "clair" || e.Nouveaux == "clair" || e.Nouveaux == "empreinte");
    }

    [Fact]
    public void CaptureChanges_RightsAuditTable_IsExcludedFromCentralJournal()
    {
        // Arrange
        using var db = CreateContext();
        db.Add(new AuditDroit { LoginOperateur = "admin" });

        // Act
        Capture(db);

        // Assert
        AuditEntries(db).Should().BeEmpty();
    }

    private static SoftTimeDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimeAuditMaskingTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    private static void Capture(SoftTimeDbContext db)
        => new AuditJournalSaveChangesInterceptor(new TestUser(), new TestCompany(), Options.Create(new AuditJournalOptions())).CaptureChanges(db);

    private static List<JournalAudit> AuditEntries(SoftTimeDbContext db)
        => db.ChangeTracker.Entries<JournalAudit>().Select(entry => entry.Entity).ToList();

    private sealed class TestUser : ICurrentUser
    {
        public decimal? UserId => 1;
        public string? Login => "agent";
        public decimal? RoleId => 1;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => true;
    }

    private sealed class TestCompany : ICompanyContext
    {
        public string? SageDatabase => "SAGE_TEST";
        public string? PointeuseDatabase => "PTE_TEST";
        public void Set(string sage, string? pointeuse) { }
    }
}
