using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
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
    public void Listed_and_pattern_columns_are_sensitive(string table, string column)
        => Assert.True(AuditMaskingPolicy.IsSensitive(table, column));

    [Theory]
    [InlineData("T_RESPONSABLE", "EMAIL")]
    [InlineData("T_RESPONSABLE", "MATRICULE")]
    [InlineData("T_RESPONSABLE", "LOGIN")]
    [InlineData("T_BDD_SAGE", "NOM_BD")]
    [InlineData("T_FERIE", "SERVEUR")]
    [InlineData("T_FERIE", null)]
    public void Email_matricule_and_ordinary_columns_stay_visible(string table, string? column)
        => Assert.False(AuditMaskingPolicy.IsSensitive(table, column));

    [Fact]
    public void Mask_value_keeps_null_and_masks_sensitive_values_only()
    {
        Assert.Null(AuditMaskingPolicy.MaskValue("T_BDD_SAGE", "TMDP", null));
        Assert.Equal(AuditMaskingPolicy.Mask, AuditMaskingPolicy.MaskValue("T_BDD_SAGE", "TMDP", "clair"));
        Assert.Equal("a@b.test", AuditMaskingPolicy.MaskValue("T_RESPONSABLE", "EMAIL", "a@b.test"));
    }

    [Fact]
    public void Mask_key_masks_only_sensitive_key_columns()
    {
        Assert.Equal("ID=3;TMDP=********", AuditMaskingPolicy.MaskKey("T_BDD_SAGE", "ID=3;TMDP=clair"));
        Assert.Equal("IDRESPONSABLE=14", AuditMaskingPolicy.MaskKey("T_RESPONSABLE", "IDRESPONSABLE=14"));
        Assert.Null(AuditMaskingPolicy.MaskKey("T_RESPONSABLE", null));
    }

    [Fact]
    public void Every_secret_like_column_of_the_application_context_is_masked()
    {
        using var db = CreateContext();
        var columns = db.Model.GetEntityTypes()
            .Select(type => (Table: type.GetTableName() ?? type.ClrType.Name, Type: type))
            .SelectMany(x => x.Type.GetProperties().Select(p => (x.Table, Column: p.GetColumnName() ?? p.Name, Property: p.Name)))
            .ToList();
        Assert.NotEmpty(columns);

        var unmasked = columns
            .Where(c => SecretLikeFragments.Any(f => c.Column.Contains(f, StringComparison.OrdinalIgnoreCase)
                                                  || c.Property.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !AuditMaskingPolicy.IsSensitive(c.Table, c.Column))
            .Select(c => $"{c.Table}.{c.Column}")
            .ToList();

        Assert.Empty(unmasked);
        // Les identifiants de connexion des bases sont bien présents dans le modèle et couverts par la liste explicite.
        Assert.Contains(columns, c => c.Table == "T_BDD_SAGE" && c.Column == "TMDP");
        Assert.Contains(columns, c => c.Table == "T_BDD_POINTEUSE" && c.Column == "SERVEUR");
    }

    [Fact]
    public void Database_credentials_are_masked_but_the_changed_column_stays_visible()
    {
        using var db = CreateContext();
        var entity = new T_BDD_SAGE { ID = 3, NOM_BD = "BASE", SERVEUR = "srv-avant", TLOGIN = "l-avant", TMDP = "m-avant" };
        db.Attach(entity);
        entity.SERVEUR = "srv-apres";
        entity.TLOGIN = "l-apres";
        entity.TMDP = "m-apres";

        Capture(db);

        var entries = AuditEntries(db);
        Assert.Equal(new[] { "SERVEUR", "TLOGIN", "TMDP" }, entries.Select(e => e.COLONNES).OrderBy(c => c).ToArray());
        Assert.All(entries, e =>
        {
            Assert.Equal(AuditMaskingPolicy.Mask, e.ANCIENS);
            Assert.Equal(AuditMaskingPolicy.Mask, e.NOUVEAUX);
        });
    }

    [Fact]
    public void Pointeuse_password_is_masked_and_email_stays_visible()
    {
        using var db = CreateContext();
        db.Add(new T_BDD_POINTEUSE { ID = 9, NOM_BD = "PTG", TMDP = "clair" });
        db.Add(new T_RESPONSABLE { LOGIN = "agent", EMAIL = "agent@exemple.test", PASSWORD = "empreinte" });

        Capture(db);

        var entries = AuditEntries(db);
        Assert.Equal(AuditMaskingPolicy.Mask, Assert.Single(entries, e => e.TABLES == "T_BDD_POINTEUSE" && e.COLONNES == "TMDP").NOUVEAUX);
        Assert.Equal(AuditMaskingPolicy.Mask, Assert.Single(entries, e => e.COLONNES == "PASSWORD").NOUVEAUX);
        Assert.Equal("agent@exemple.test", Assert.Single(entries, e => e.COLONNES == "EMAIL").NOUVEAUX);
        Assert.DoesNotContain(entries, e => e.ANCIENS == "clair" || e.NOUVEAUX == "clair" || e.NOUVEAUX == "empreinte");
    }

    [Fact]
    public void Rights_audit_table_is_excluded_from_the_central_journal()
    {
        using var db = CreateContext();
        db.Add(new T_AUDIT_DROIT { LOGIN_OPERATEUR = "admin" });

        Capture(db);

        Assert.Empty(AuditEntries(db));
    }

    private static SoftTimeDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimeAuditMaskingTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    private static void Capture(SoftTimeDbContext db)
        => new AuditJournalSaveChangesInterceptor(new TestUser(), new TestCompany(), Options.Create(new AuditJournalOptions())).CaptureChanges(db);

    private static List<suivi_JOURNAL> AuditEntries(SoftTimeDbContext db)
        => db.ChangeTracker.Entries<suivi_JOURNAL>().Select(entry => entry.Entity).ToList();

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
