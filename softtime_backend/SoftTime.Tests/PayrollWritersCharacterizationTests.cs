using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Caractérisation (lot C1) : fige ce que produisent aujourd'hui les sept variables d'heures supplémentaires, avant la
/// généralisation des variables de paie. Complète <see cref="PayrollVariableWritersTests"/> (SQL, ordre, opérandes, marqueur)
/// et <see cref="PayrollCategoryMappingTests"/> (correspondance catégorie → valeur) sans les dupliquer : montants, synthèse du
/// journal, validation SAGE, préflight et refus de valeurs de la base AUTRE, test de configuration AUTRE.
/// </summary>
public class PayrollWritersCharacterizationTests
{
    private static readonly HsExoDto Ligne = new(1, "MAT01", 101, 1.5m, 2m, 0.5m, 1.25m, 3m, 4m, 2.5m, 7m, 1m);

    // ---------- Liste et montants ----------

    [Fact]
    public void Categories_AreTheSevenOvertimeVariablesInHistoricalOrder()
        => PayrollWritingConfiguration.Categories.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit");

    [Fact]
    public void OvertimeAmounts_From_ReadsTheSevenColumns()
    {
        // Act
        var montants = OvertimeAmounts.From(Ligne);

        // Assert : EXO 130, EXO 150, IMPO 130, IMPO 150, Férié, Dimanche, Nuit (colonnes EXO130, EXO150, I130, I150,
        // FERIE, DIM, NUIT de la ligne).
        PayrollWritingConfiguration.Categories.Select(montants.ValeurDe).Should().Equal(1.5m, 2m, 0.5m, 1.25m, 3m, 2.5m, 4m);
    }

    [Fact]
    public void OvertimeAmounts_From_MissingValuesAreZero()
    {
        var montants = OvertimeAmounts.From(new HsExoDto(2, "MAT02", 102, null, null, null, null, null, null, null, null, null));

        PayrollWritingConfiguration.Categories.Select(montants.ValeurDe).Should().AllBeEquivalentTo(0m);
    }

    // ---------- STANDARD : synthèse du journal et message ----------

    [Fact]
    public async Task StandardWrite_Default_AuditSummaryListsTheSevenCategories()
    {
        // Arrange
        var audit = new CapturingAudit();

        // Act
        var resultat = await new TestStandardWriter(new CapturingSagePayrollWriter(), audit).WriteAsync(Base(), [Ligne, Ligne with { Oid = 2, Matricule = "MAT02", NumSal = 102 }], Contexte());

        // Assert
        var entree = audit.Entrees.Should().ContainSingle().Which;
        entree.TargetTable.Should().Be("T_CUMSAL");
        entree.Source.Should().Be("SYNC_PAIE:STANDARD");
        entree.Status.Should().Be("SUCCES");
        entree.Employees.Should().Be(2);
        entree.UpdatedRows.Should().Be(14);
        entree.UpdatesByCategory.Keys.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit");
        entree.UpdatesByCategory.Values.Should().AllBeEquivalentTo(2);
        resultat.Should().Be(new PayrollWriteResultDto("SAGE_TEST", "STANDARD", 2, 14, "Synchronisation des heures supplémentaires vers SAGE terminée."));
    }

    [Fact]
    public async Task StandardWrite_RowWithoutNumSal_IsSkippedAndNotCounted()
    {
        // Arrange
        var sage = new CapturingSagePayrollWriter();

        // Act
        var resultat = await new TestStandardWriter(sage, new CapturingAudit()).WriteAsync(Base(), [Ligne, Ligne with { NumSal = null }], Contexte());

        // Assert
        sage.Calls.Should().HaveCount(7);
        resultat.EmployeesUpdated.Should().Be(1);
    }

    // ---------- STANDARD : validation de configuration ----------

    [Fact]
    public async Task StandardValidate_AllConstantsPresent_IsValid_CaseInsensitive()
    {
        // Arrange
        var sut = Validateur(Enumerable.Range(1, 7).Select(i => new ConstanteSage { CodeConstante = $"hs0{i} ", CodeOperande1 = (short)i }));

        // Act
        var resultat = await sut.ValidateAsync(Base(), [Ligne]);

        // Assert
        resultat.Should().Be(new PayrollWriteResultDto("SAGE_TEST", "STANDARD", 0, 0, "Configuration SAGE valide."));
    }

    [Fact]
    public async Task StandardValidate_MissingConstants_ReportsTheFirstMissingInHistoricalOrder()
    {
        // Arrange : HS03 et HS06 absentes.
        var sut = Validateur(new[] { 1, 2, 4, 5, 7 }.Select(i => new ConstanteSage { CodeConstante = $"HS0{i}", CodeOperande1 = (short)i }));

        // Act
        var act = () => sut.ValidateAsync(Base(), [Ligne]);

        // Assert
        await act.Should().ThrowExactlyAsync<DomainException>().WithMessage("Constante SAGE HS03 introuvable.");
    }

    // ---------- AUTRE : synthèse du journal ----------

    [Fact]
    public async Task OtherWrite_Default_AuditSummaryListsTheSevenCategories()
    {
        // Arrange
        var (sut, _, audit) = Autre();

        // Act
        await sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        var entree = audit.Entrees.Should().ContainSingle().Which;
        entree.TargetTable.Should().Be("dbo.PAIE_CUMULS");
        entree.Source.Should().Be("SYNC_PAIE:AUTRE");
        entree.UpdatesByCategory.Keys.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit");
        entree.UpdatedRows.Should().Be(7);
    }

    [Fact]
    public async Task OtherWrite_Failure_AuditCountsCancelledRows()
    {
        // Arrange : la troisième mise à jour ne trouve pas sa ligne.
        var (sut, db, audit) = Autre();
        var n = 0;
        db.Affected = (_, _) => ++n == 3 ? 0 : 1;

        // Act
        var act = () => sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        await act.Should().ThrowExactlyAsync<DomainException>()
            .WithMessage("La synchronisation a été annulée : 0 ligne(s) mise(s) à jour pour le matricule MAT01 et la catégorie I130; une seule ligne était attendue.");
        var entree = audit.Entrees.Should().ContainSingle().Which;
        entree.Status.Should().Be("ECHEC");
        entree.UpdatedRows.Should().Be(0);
        entree.CancelledRows.Should().Be(2);
    }

    // ---------- AUTRE : préflight ----------

    [Fact]
    public async Task OtherPreflight_TableMissing_IsRefused()
    {
        var (sut, db, _) = Autre();
        db.Columns.Clear();

        await FluentActions.Awaiting(() => sut.ValidateAsync(BaseAutre(), [Ligne]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("La table PAIE_CUMULS n'existe pas.");
    }

    [Fact]
    public async Task OtherPreflight_MatriculeColumnMissing_IsRefused()
    {
        var (sut, db, _) = Autre();
        db.Columns.Remove("MatriculeEmploye");

        await FluentActions.Awaiting(() => sut.ValidateAsync(BaseAutre(), [Ligne]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("La colonne MatriculeEmploye n'existe pas.");
    }

    [Fact]
    public async Task OtherPreflight_ValueColumnMissingOrNotNumeric_IsRefused()
    {
        var (manquante, dbManquante, _) = Autre();
        dbManquante.Columns.Remove("HeuresNuit");
        var (texte, dbTexte, _) = Autre();
        dbTexte.Columns["HeuresFerie"] = new OtherPayrollColumn("nvarchar", null, null);

        await FluentActions.Awaiting(() => manquante.ValidateAsync(BaseAutre(), [Ligne]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("La colonne HeuresNuit de la catégorie Nuit n'existe pas.");
        await FluentActions.Awaiting(() => texte.ValidateAsync(BaseAutre(), [Ligne]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("La colonne HeuresFerie de la catégorie Ferie doit être numérique.");
    }

    [Theory]
    [InlineData("int", null, null, 1.5, false)]
    [InlineData("int", null, null, 2.0, true)]
    [InlineData("decimal", (byte)4, 1, 1.25, false)]
    [InlineData("decimal", (byte)4, 2, 1.25, true)]
    // Défaut latent figé tel quel : 12,5 est accepté pour decimal(3,2) (chiffres entiers + décimales de la VALEUR ≤ précision),
    // alors que SQL Server n'y stocke que 9,99 au plus. Signalé, non corrigé en C1 (aucun changement de comportement).
    [InlineData("decimal", (byte)3, 2, 12.5, true)]
    [InlineData("decimal", (byte)3, 2, 123.5, false)]
    [InlineData("float", null, null, 1.234567, true)]
    public async Task OtherPreflight_CanStore_ChecksIntegerAndDecimalCapacity(string type, byte? precision, int? scale, double valeur, bool accepte)
    {
        // Arrange : la colonne EXO 130 reçoit la valeur testée.
        var (sut, db, _) = Autre();
        db.Columns["HeuresExo130"] = new OtherPayrollColumn(type, precision, scale);
        var ligne = Ligne with { Exo130 = (decimal)valeur };

        // Act
        var act = () => sut.ValidateAsync(BaseAutre(), [ligne]);

        // Assert
        if (accepte)
            await act.Should().NotThrowAsync();
        else
            await act.Should().ThrowExactlyAsync<DomainException>().WithMessage($"La valeur {(decimal)valeur} du matricule MAT01 n'est pas compatible avec la colonne HeuresExo130.");
    }

    [Fact]
    public async Task OtherPreflight_MatriculeRowsAndDuplicates_AreRefused()
    {
        var (aucune, dbAucune, _) = Autre();
        dbAucune.LignesParMatricule = _ => 0;
        var (plusieurs, dbPlusieurs, _) = Autre();
        dbPlusieurs.LignesParMatricule = _ => 2;
        var (doublon, _, _) = Autre();

        await FluentActions.Awaiting(() => aucune.ValidateAsync(BaseAutre(), [Ligne]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("Aucune ligne de paie trouvée pour le matricule MAT01.");
        await FluentActions.Awaiting(() => plusieurs.ValidateAsync(BaseAutre(), [Ligne]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("Plusieurs lignes de paie correspondent au matricule MAT01.");
        await FluentActions.Awaiting(() => doublon.ValidateAsync(BaseAutre(), [Ligne, Ligne with { Oid = 2 }]))
            .Should().ThrowExactlyAsync<DomainException>().WithMessage("Plusieurs résultats d'heures supplémentaires correspondent au matricule MAT01.");
    }

    [Fact]
    public async Task OtherPreflight_RetardAndAbsenceValuesAreNeverChecked()
    {
        // Arrange : colonnes entières, Retard et Absence décimaux ; aucune colonne pour eux dans T_PAIE_ECRITURE.
        var (sut, _, _) = Autre();

        // Act
        var resultat = await sut.ValidateAsync(BaseAutre(), [Ligne with { Retard = 0.33m, Absence = 7.5m }]);

        // Assert
        resultat.Message.Should().Be("Configuration d'écriture valide.");
    }

    // ---------- Test de configuration AUTRE (PayrollWriteConfigurationService) ----------

    [Fact]
    public async Task TestConfiguration_Default_Messages()
    {
        // Arrange
        var complet = await TesterConfiguration(Configuration(), tables: ["PAIE_CUMULS"], colonnes: Colonnes());
        var incomplet = await TesterConfiguration(Configuration().Where(c => c.Categorie != "I150").ToList(), ["PAIE_CUMULS"], Colonnes());
        var tableAbsente = await TesterConfiguration(Configuration(), [], Colonnes());
        var colonneAbsente = await TesterConfiguration(Configuration(), ["PAIE_CUMULS"], Colonnes().Where(c => c != "HeuresDimanche").ToList());

        // Assert
        complet.Should().Be(new ConnectionTestResultDto(true, "Configuration d'écriture valide."));
        incomplet.Should().Be(new ConnectionTestResultDto(false, "La configuration d'écriture de la base AUTRE est incomplète : la catégorie I150 n'est pas configurée."));
        tableAbsente.Should().Be(new ConnectionTestResultDto(false, "La table PAIE_CUMULS n'existe pas."));
        colonneAbsente.Should().Be(new ConnectionTestResultDto(false, "La colonne HeuresDimanche n'existe pas."));
    }

    // ---------- Aides ----------

    private static BddSage Base() => new() { NomBd = "SAGE_TEST", TypeBase = "STANDARD" };
    private static BddSage BaseAutre() => new() { NomBd = "SAGE_AUTRE", TypeBase = "AUTRE" };
    private static PayrollSyncContext Contexte() => new(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "rh", Guid.NewGuid());

    private static SagePayrollTarget Cible() => new("test",
        Enumerable.Range(1, 7).Select(i => new ConstanteSage { CodeConstante = $"HS0{i}", CodeOperande1 = (short)i }).ToArray(),
        new OvertimeSageCodes());

    private static List<PaieEcriture> Configuration() => PayrollWritingConfiguration.Categories
        .Select(c => new PaieEcriture { BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye", Categorie = c, ColValeur = $"Heures{c}" })
        .ToList();

    private static List<string> Colonnes() => [.. PayrollWritingConfiguration.Categories.Select(c => $"Heures{c}"), "MatriculeEmploye"];

    private static StandardPayrollWriter Validateur(IEnumerable<ConstanteSage> constantes)
    {
        var contexte = new Mock<IDisposableSageContext>();
        contexte.SetupGet(c => c.Constants).Returns(constantes.ToList().AsQueryable());
        var fabrique = new Mock<ISageContextFactory>();
        fabrique.Setup(f => f.Create(It.IsAny<string>())).Returns(contexte.Object);
        var tenant = new Mock<ITenantConnectionService>();
        tenant.Setup(t => t.GetSageConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test");
        var codes = new Mock<ICodeConstanteService>();
        codes.Setup(c => c.GetOvertimeSageCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new OvertimeSageCodes());
        return new StandardPayrollWriter(new CapturingSagePayrollWriter(), fabrique.Object, tenant.Object, codes.Object, new CapturingAudit());
    }

    private static (OtherPayrollWriter Sut, FakeOtherPayrollDatabase Db, CapturingAudit Audit) Autre()
    {
        var db = new FakeOtherPayrollDatabase(new List<string>());
        db.Columns["MatriculeEmploye"] = new OtherPayrollColumn("nvarchar", null, null);
        foreach (var c in PayrollWritingConfiguration.Categories)
            db.Columns[$"Heures{c}"] = new OtherPayrollColumn("decimal", 18, 4);
        var uow = new InMemoryUnitOfWork();
        uow.Get<PaieEcriture>().Items.AddRange(Configuration());
        var audit = new CapturingAudit();
        var sut = new OtherPayrollWriter(uow, Mock.Of<ITenantConnectionService>(), audit, NullLogger<OtherPayrollWriter>.Instance,
            null, null, _ => Task.FromResult<IOtherPayrollDatabase>(db));
        return (sut, db, audit);
    }

    private static async Task<ConnectionTestResultDto> TesterConfiguration(List<PaieEcriture> configs, List<string> tables, List<string> colonnes)
    {
        var uow = new InMemoryUnitOfWork();
        uow.Get<PaieEcriture>().Items.AddRange(configs);
        var tenant = new Mock<ITenantConnectionService>();
        tenant.SetupGet(t => t.SageDb).Returns("SAGE_AUTRE");
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(BaseAutre());
        var discovery = new Mock<IExternalDiscoveryService>();
        discovery.Setup(d => d.ListTablesAsync(It.IsAny<SqlServerCredentials>(), "SAGE_AUTRE", It.IsAny<CancellationToken>())).ReturnsAsync(tables);
        discovery.Setup(d => d.ListColumnsAsync(It.IsAny<SqlServerCredentials>(), "SAGE_AUTRE", "PAIE_CUMULS", It.IsAny<CancellationToken>())).ReturnsAsync(colonnes);
        return await new PayrollWriteConfigurationService(uow, tenant.Object, discovery.Object).TestPayrollWriteConfigurationAsync();
    }

    private sealed class TestStandardWriter(ISagePayrollWriter sageWriter, IAuditTrail auditTrail)
        : StandardPayrollWriter(sageWriter, null!, null!, null!, auditTrail)
    {
        protected override Task<SagePayrollTarget> GetInputAsync(CancellationToken ct) => Task.FromResult(Cible());
    }

    private sealed class CapturingAudit : IAuditTrail
    {
        public List<PayrollSyncAudit> Entrees { get; } = new();

        public Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default)
        {
            Entrees.Add(audit);
            return Task.CompletedTask;
        }
    }
}
