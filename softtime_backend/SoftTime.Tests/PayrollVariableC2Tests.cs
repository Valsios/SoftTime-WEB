using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Lot C2 : Retard envoyable. Base SAGE : code choisi dans T_CST de la base active. Autre base : destination dans
/// T_PAIE_ECRITURE, colonne vérifiée dans la base de paie (existe, numérique, accepte les décimales). Mêmes droits, même
/// verrou du mois envoyé, même journal, aucun effet sur les autres bases, comportement par défaut inchangé.
/// Tests unitaires (unité de travail en mémoire) : aucun modèle d'intégration LocalDB n'existe pour l'écriture de paie.
/// </summary>
public class PayrollVariableC2Tests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    // ---------- Défaut inchangé ----------

    [Fact]
    public void Default_SendsTheSevenOvertimeVariablesOnly()
    {
        PayrollVariableSelection.Default.Envoyees.Should().Equal(PayrollVariableSelection.HeuresSup);
        PayrollVariableSelection.Default.EstActive(PayrollVariableSelection.Retard).Should().BeFalse();
        PayrollVariableSelection.Default.CodesPaie.Should().BeEmpty();
    }

    [Fact]
    public void Catalog_RetardIsSendableInHours_AbsenceInHoursOrDays()
    {
        // Adapté au lot C3 (avant : Absence non envoyable).
        var retard = PayrollVariableCatalog.Definition(PayrollVariableSelection.Retard);
        var absence = PayrollVariableCatalog.Definition(PayrollVariableSelection.Absence);

        retard.Envoyable.Should().BeTrue();
        retard.Unites.Should().Equal(PayrollUnits.Heures);
        retard.CodeCommun.Should().BeFalse("le code de Retard est propre à chaque base");
        retard.ValeurEnvoyee(new PayrollRowValues(1, 2, 3, 4, 5, 6, 7, 2.75m, 9)).Should().Be(2.75m);
        absence.Envoyable.Should().BeTrue();
        absence.Unites.Should().Equal(PayrollUnits.Heures, PayrollUnits.Jours);
        PayrollVariableSelection.SansEnvoi.Should().BeEmpty();
    }

    // ---------- Base SAGE : code de Retard ----------

    [Fact]
    public async Task Save_Sage_RetardWithACodeFromTCst_IsStoredAsInSageInHours_AndSent()
    {
        // Arrange
        var c = Creer(codes: [new("RET01", "Retard")]);

        // Act
        var resultat = await c.Sut.SaveAsync(Saisie(("Retard", true, "ret01", null)));

        // Assert
        var ligne = c.Uow.Get<PaieVariable>().Items.Single(v => v.Variable == "Retard");
        ligne.CodePaie.Should().Be("RET01");
        ligne.Unite.Should().Be(PayrollUnits.Heures);
        ligne.ColCible.Should().BeNull();
        var selection = await c.Sut.GetSelectionAsync("SAGE_TEST");
        selection.Envoyees.Should().Equal([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Retard]);
        selection.CodeDe(PayrollVariableSelection.Retard).Should().Be("RET01");
        resultat.Variables.Single(v => v.Variable == "Retard").Should().Match<PayrollVariableDto>(v => v.Actif && v.CodePaie == "RET01" && !v.SansEnvoi);
    }

    [Fact]
    public async Task Save_Sage_RetardCodeAbsentFromTCstOfTheActiveBase_IsRefusedAndChangesNothing()
    {
        // Arrange : HS01 existe, XX99 non.
        var c = Creer(codes: [new("HS01", "EXO 130")]);

        // Act
        var act = () => c.Sut.SaveAsync(Saisie(("Retard", true, "XX99", "H")));

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be("Le code de paie XX99 de la variable Retard n'existe pas dans la base SAGE SAGE_TEST.");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_Sage_TCstUnreadable_IsRefused()
    {
        // Arrange
        var c = Creer(codesIllisibles: true);

        // Act
        var act = () => c.Sut.SaveAsync(Saisie(("Retard", true, "RET01", "H")));

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("n'ont pas pu être lus");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_Sage_RetardUncheckedWithoutCode_DoesNotReadTCst()
    {
        // Arrange : aucun code à vérifier, T_CST illisible sans conséquence.
        var c = Creer(codesIllisibles: true);

        // Act
        await c.Sut.SaveAsync(Saisie(("Nuit", false, null, null)));

        // Assert
        c.Uow.Get<PaieVariable>().Items.Should().HaveCount(9);
    }

    [Fact]
    public async Task Save_AbsenceChecked_WithACodeUnknownToSage_IsRefused()
    {
        // Adapté au lot C3 (avant : Absence cochée refusée, « prochaine étape ») : refusée seulement si le code est inconnu.
        var c = Creer();

        var act = () => c.Sut.SaveAsync(Saisie(("Absence", true, "ABS01", "J")));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be("Le code de paie ABS01 de la variable Absence n'existe pas dans la base SAGE SAGE_TEST.");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConfiguration_Sage_ListsTheCodesReadInSage()
    {
        var c = Creer(codes: [new("HS01", "EXO 130"), new("RET01", "Retard")]);

        var config = await c.Sut.GetConfigurationAsync();

        config.CodesPaie.Select(o => o.Code).Should().Equal("HS01", "RET01");
        config.AvertissementCodes.Should().BeNull();
    }

    [Fact]
    public async Task GetConfiguration_Sage_CodesUnreadable_EmptyListWithAWarning()
    {
        var c = Creer(codesIllisibles: true);

        var config = await c.Sut.GetConfigurationAsync();

        config.CodesPaie.Should().BeEmpty();
        config.AvertissementCodes.Should().Be("Les codes de paie n'ont pas pu être lus dans SAGE.");
    }

    [Fact]
    public async Task GetActives_CheckedAbsence_IsReportedAsSent_WithItsUnit()
    {
        // Arrange : adapté au lot C3 (avant : Absence active héritée du lot B, jamais annoncée comme envoyée).
        var c = Creer();
        c.Uow.Get<PaieVariable>().Items.Add(new PaieVariable { BddSage = "SAGE_TEST", Variable = "Absence", Actif = true, CodePaie = "ABS01", Unite = "J" });

        // Act
        var actives = await c.Sut.GetActivesAsync();

        // Assert
        actives.Actives.Should().Equal([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Absence]);
        actives.UniteAbsence.Should().Be("J");
        actives.HeuresParJour.Should().Be(8m);
    }

    // ---------- Autre base : Retard dans la configuration des variables ----------

    [Fact]
    public async Task Save_Other_RetardCheckedWithoutDestination_IsRefused()
    {
        var c = Creer(typeBase: "AUTRE");

        var act = () => c.Sut.SaveAsync(Saisie(("Retard", true, null, "H")));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("Retard ne peut pas être cochée tant que sa colonne cible n'est pas renseignée");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_Other_RetardCheckedWithADestination_IsAccepted_ColumnStaysInTPaieEcriture()
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.Add(Ecriture("Retard", "HeuresRetard"));

        // Act
        var resultat = await c.Sut.SaveAsync(Saisie(("Retard", true, null, null)));

        // Assert
        var ligne = c.Uow.Get<PaieVariable>().Items.Single(v => v.Variable == "Retard");
        ligne.ColCible.Should().BeNull();
        ligne.CodePaie.Should().BeNull();
        ligne.Unite.Should().Be("H");
        resultat.Variables.Single(v => v.Variable == "Retard").CibleExistante.Should().Be("HeuresRetard");
    }

    // ---------- Autre base : destinations ----------

    [Fact]
    public async Task GetDestinations_Other_ListsTheSendableVariablesTablesAndTypedColumns()
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());

        // Act
        var destinations = await c.Sut.GetDestinationsAsync();

        // Assert
        destinations.Table.Should().Be("PAIE_CUMULS");
        destinations.ColonneMatricule.Should().Be("Matricule");
        // Adapté au lot C3 : Absence est une variable envoyable (deux décimales, comme Retard).
        destinations.Variables.Select(v => v.Variable).Should().Equal([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Retard, PayrollVariableSelection.Absence]);
        destinations.Variables.Single(v => v.Variable == "Retard").Should().Be(new PayrollWriteTargetDto("Retard", "Retard", false, null, 2));
        destinations.Variables.Last().Should().Be(new PayrollWriteTargetDto("Absence", "Absence", false, null, 2));
        destinations.Tables.Should().Contain("PAIE_CUMULS");
        destinations.Colonnes.Single(col => col.Nom == "HeuresRetard").Should().Be(new PayrollTargetColumnDto("HeuresRetard", "decimal(18,2)", true, true));
        destinations.Colonnes.Single(col => col.Nom == "Entier").Should().Be(new PayrollTargetColumnDto("Entier", "int", true, false));
        destinations.Colonnes.Single(col => col.Nom == "Libelle").Numerique.Should().BeFalse();
    }

    [Fact]
    public async Task SaveDestinations_Other_RetardInADecimalColumn_AddsTheRow_KeepsTheOthers()
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());
        var avant = c.Uow.Get<PaieEcriture>().Items.Select(e => (e.Categorie, e.ColValeur)).ToList();

        // Act
        var resultat = await c.Sut.SaveDestinationsAsync(Destinations(("Retard", "heuresretard")));

        // Assert : nom de colonne tel qu'il figure dans la base de paie.
        var retard = c.Uow.Get<PaieEcriture>().Items.Single(e => e.Categorie == "Retard");
        retard.Should().Match<PaieEcriture>(e => e.BddSage == "SAGE_AUTRE" && e.TableCible == "PAIE_CUMULS" && e.ColMatricule == "Matricule" && e.ColValeur == "HeuresRetard");
        c.Uow.Get<PaieEcriture>().Items.Where(e => e.Categorie != "Retard").Select(e => (e.Categorie, e.ColValeur)).Should().Equal(avant);
        resultat.Variables.Single(v => v.Variable == "Retard").Colonne.Should().Be("HeuresRetard");
    }

    [Theory]
    [InlineData("Inexistante", "La colonne Inexistante de la variable Retard n'existe pas dans la table PAIE_CUMULS.")]
    [InlineData("Libelle", "La colonne Libelle de la variable Retard n'est pas numérique (nvarchar).")]
    [InlineData("Entier", "La colonne Entier de la variable Retard n'accepte pas 2 décimales (int) : la valeur est un nombre d'heures avec décimales.")]
    [InlineData("UneDecimale", "La colonne UneDecimale de la variable Retard n'accepte pas 2 décimales (decimal(9,1)) : la valeur est un nombre d'heures avec décimales.")]
    public async Task SaveDestinations_Other_InvalidRetardColumn_IsRefusedAndChangesNothing(string colonne, string message)
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());
        var avant = c.Uow.Snapshot();

        // Act
        var act = () => c.Sut.SaveDestinationsAsync(Destinations(("Retard", colonne)));

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be(message);
        c.Uow.Snapshot().Should().Be(avant);
    }

    [Fact]
    public async Task SaveDestinations_Other_SameColumnForTwoVariables_IsRefused()
    {
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());

        var act = () => c.Sut.SaveDestinationsAsync(Destinations(("Retard", "HeuresNuit")));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("La colonne HeuresNuit est choisie pour plusieurs variables (Nuit, Retard).");
    }

    [Fact]
    public async Task SaveDestinations_Other_SentVariableWithoutColumn_IsRefused()
    {
        // Arrange : Nuit est envoyée par défaut.
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());

        // Act
        var act = () => c.Sut.SaveDestinationsAsync(Destinations(("Nuit", null)));

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("La variable Nuit est envoyée à la paie : choisissez sa colonne de destination.");
        c.Uow.Get<PaieEcriture>().Items.Should().HaveCount(7);
    }

    [Fact]
    public async Task SaveDestinations_Other_UncheckedVariableWithoutColumn_RemovesItsRow()
    {
        // Arrange : Retard configurée mais non cochée.
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange([.. Configuration(), Ecriture("Retard", "HeuresRetard")]);

        // Act
        await c.Sut.SaveDestinationsAsync(Destinations(("Retard", null)));

        // Assert
        c.Uow.Get<PaieEcriture>().Items.Select(e => e.Categorie).Should().Equal(PayrollVariableSelection.HeuresSup);
    }

    [Fact]
    public async Task SaveDestinations_Absence_InADecimalColumn_IsAccepted()
    {
        // Adapté au lot C3 (avant : Absence refusée, « prochaine étape »).
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());

        var resultat = await c.Sut.SaveDestinationsAsync(Destinations(("Absence", "HeuresRetard")));

        c.Uow.Get<PaieEcriture>().Items.Single(e => e.Categorie == "Absence").ColValeur.Should().Be("HeuresRetard");
        resultat.Variables.Single(v => v.Variable == "Absence").Colonne.Should().Be("HeuresRetard");
    }

    [Fact]
    public async Task SaveDestinations_TableOrMatriculeMissingInThePayrollBase_IsRefused()
    {
        var c = Creer(typeBase: "AUTRE");

        var table = () => c.Sut.SaveDestinationsAsync(new PayrollWriteTargetsSaveDto("INCONNUE", "Matricule", []));
        var matricule = () => c.Sut.SaveDestinationsAsync(new PayrollWriteTargetsSaveDto("PAIE_CUMULS", "Inconnue", []));

        (await table.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("La table INCONNUE n'existe pas dans la base de paie.");
        (await matricule.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("La colonne matricule Inconnue n'existe pas dans la table PAIE_CUMULS.");
        c.Uow.Get<PaieEcriture>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveDestinations_SchemaUnreadable_IsRefusedAndChangesNothing()
    {
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());
        c.Schema.Illisible = true;

        var act = () => c.Sut.SaveDestinationsAsync(Destinations(("Retard", "HeuresRetard")));

        await act.Should().ThrowAsync<ExternalConnectionException>();
        c.Uow.Get<PaieEcriture>().Items.Should().HaveCount(7);
        (await c.Sut.GetDestinationsAsync()).Colonnes.Should().BeEmpty("l'affichage reste possible, sans liste");
    }

    [Fact]
    public async Task SaveDestinations_CurrentMonthAlreadySent_IsRefusedAndChangesNothing()
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(Configuration());
        c.Envois.Periodes.Add((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));

        // Act
        var act = () => c.Sut.SaveDestinationsAsync(Destinations(("Retard", "HeuresRetard")));

        // Assert
        (await act.Should().ThrowExactlyAsync<ConflictException>()).Which.Message.Should().StartWith("Mois déjà envoyé en paie (période 2026-11-01 → 2026-11-30)");
        c.Uow.Get<PaieEcriture>().Items.Should().HaveCount(7);
        (await c.Sut.GetDestinationsAsync()).MoisEnvoye.Should().BeTrue();
    }

    [Fact]
    public async Task Destinations_WithoutTheRightToModifyTheBase_AreForbidden()
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        c.Autorisation.Setup(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "SAGE_AUTRE", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("refus"));

        // Act
        var lecture = () => c.Sut.GetDestinationsAsync();
        var colonnes = () => c.Sut.GetColonnesAsync("PAIE_CUMULS");
        var ecriture = () => c.Sut.SaveDestinationsAsync(Destinations(("Retard", "HeuresRetard")));

        // Assert
        await lecture.Should().ThrowExactlyAsync<ForbiddenException>();
        await colonnes.Should().ThrowExactlyAsync<ForbiddenException>();
        await ecriture.Should().ThrowExactlyAsync<ForbiddenException>();
        c.Uow.Get<PaieEcriture>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Destinations_OnASageBase_AreRefused()
    {
        var c = Creer(typeBase: "STANDARD");

        var act = () => c.Sut.SaveDestinationsAsync(Destinations(("Retard", "HeuresRetard")));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("uniquement pour une base AUTRE");
    }

    [Fact]
    public async Task SaveDestinations_DoesNotTouchAnotherBase_AndInvalidatesTheCaches()
    {
        // Arrange
        var c = Creer(typeBase: "AUTRE");
        var autreBase = Ecriture("Retard", "AutreColonne");
        autreBase.BddSage = "SAGE_VOISINE";
        c.Uow.Get<PaieEcriture>().Items.AddRange([.. Configuration(), autreBase]);
        (await c.Sut.GetSelectionAsync("SAGE_AUTRE")).Should().BeSameAs(PayrollVariableSelection.Default);
        var cle = new ClosingChecklistRequest(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null);
        c.Checklist.Set("cle", new ClosingChecklistDto(cle.From, cle.To, null, []), Maintenant, TimeSpan.FromMinutes(5));

        // Act
        await c.Sut.SaveDestinationsAsync(Destinations(("Retard", "HeuresRetard")));

        // Assert
        autreBase.Should().Match<PaieEcriture>(e => e.BddSage == "SAGE_VOISINE" && e.ColValeur == "AutreColonne");
        c.Checklist.TryGet("cle", Maintenant, out _).Should().BeFalse("le cache du Contrôle avant paie est vidé");
    }

    [Fact]
    public async Task GetColonnes_ListsTheTypedColumnsOfADboTable()
    {
        var c = Creer(typeBase: "AUTRE");

        var colonnes = await c.Sut.GetColonnesAsync("dbo.PAIE_CUMULS");
        var autreSchema = () => c.Sut.GetColonnesAsync("rh.PAIE_CUMULS");

        colonnes.Select(col => col.Nom).Should().Contain(["Matricule", "HeuresRetard", "Entier"]);
        (await autreSchema.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("Seules les tables du schéma dbo sont prises en charge.");
    }

    [Theory]
    [InlineData("int", null, null, 0, true)]
    [InlineData("int", null, null, 2, false)]
    [InlineData("decimal", (byte)18, 2, 2, true)]
    [InlineData("decimal", (byte)9, 1, 2, false)]
    [InlineData("numeric", (byte)10, 4, 2, true)]
    [InlineData("money", (byte)19, 4, 2, true)]
    [InlineData("float", (byte)53, null, 2, true)]
    [InlineData("nvarchar", null, null, 0, false)]
    public void AccepteDecimales_DependsOnTheColumnType(string type, byte? precision, int? echelle, int decimales, bool attendu)
        => PayrollTargetColumns.AccepteDecimales(new PayrollTargetColumn("C", type, precision, echelle), decimales).Should().Be(attendu);

    // ---------- Journal central ----------

    [Fact]
    public void Journal_ModifiedDestination_RecordsWhoBeforeAndAfter()
    {
        // Arrange : même modèle que le journal des variables (modèle EF réel, sans connexion ouverte).
        using var db = new SoftTimeDbContext(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimePayrollVariablesJournal;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        var destination = new PaieEcriture { Id = 9, BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "Matricule", Categorie = "Retard", ColValeur = "Ancienne" };
        db.Attach(destination);
        destination.ColValeur = "HeuresRetard";
        db.Entry(destination).Property(e => e.ColValeur).IsModified = true;

        // Act
        new AuditJournalSaveChangesInterceptor(new TestCurrentUser(1, 1), Mock.Of<ICompanyContext>(), Options.Create(new AuditJournalOptions()))
            .CaptureChanges(db);

        // Assert
        var entree = db.ChangeTracker.Entries<JournalAudit>().Select(e => e.Entity).Should().ContainSingle().Which;
        entree.Tables.Should().Be("T_PAIE_ECRITURE");
        entree.Anciens.Should().Be("Ancienne");
        entree.Nouveaux.Should().Be("HeuresRetard");
        entree.Users.Should().Be("u1");
    }

    // ---------- Aides ----------

    private sealed record Ctx(
        PayrollVariableService Sut, InMemoryUnitOfWork Uow, RecordingSendLog Envois, Mock<IDatabaseConfigurationAuthorizer> Autorisation,
        ClosingChecklistCache Checklist, FakePayrollTargetSchema Schema);

    private static Ctx Creer(string typeBase = "STANDARD", SageConstantOptionDto[]? codes = null, bool codesIllisibles = false)
    {
        var nom = typeBase == "AUTRE" ? "SAGE_AUTRE" : "SAGE_TEST";
        var uow = new InMemoryUnitOfWork();
        var tenant = new Mock<ITenantConnectionService>();
        tenant.SetupGet(t => t.SageDb).Returns(nom);
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { NomBd = nom, TypeBase = typeBase });
        var constantes = new Mock<ICodeConstanteService>();
        constantes.Setup(c => c.GetOvertimeSageCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new OvertimeSageCodes());
        if (codesIllisibles)
            constantes.Setup(c => c.ListSageConstantOptionsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("SAGE injoignable"));
        else
            constantes.Setup(c => c.ListSageConstantOptionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(codes ?? []);
        var schema = new FakePayrollTargetSchema();
        schema.Tables["PAIE_CUMULS"] =
        [
            new("Matricule", "nvarchar", null, null),
            .. PayrollVariableSelection.HeuresSup.Select(v => new PayrollTargetColumn($"Heures{v}", "decimal", 18, 2)),
            new("HeuresRetard", "decimal", 18, 2),
            new("Entier", "int", 10, 0),
            new("UneDecimale", "decimal", 9, 1),
            new("Libelle", "nvarchar", null, null),
        ];
        var envois = new RecordingSendLog();
        var autorisation = new Mock<IDatabaseConfigurationAuthorizer>();
        var checklist = new ClosingChecklistCache();
        var sut = new PayrollVariableService(uow, tenant.Object, autorisation.Object, new TestCurrentUser(1, 1), envois,
            constantes.Object, Mock.Of<IExternalDiscoveryService>(), new PayrollVariableCache(), new ClosingAnalysisCache(), checklist,
            new HorlogeFixe(), NullLogger<PayrollVariableService>.Instance, schema);
        return new Ctx(sut, uow, envois, autorisation, checklist, schema);
    }

    private static PaieEcriture Ecriture(string categorie, string colonne) =>
        new() { BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "Matricule", Categorie = categorie, ColValeur = colonne };

    private static List<PaieEcriture> Configuration() =>
        PayrollVariableSelection.HeuresSup.Select(v => Ecriture(v, $"Heures{v}")).ToList();

    private static PayrollWriteTargetsSaveDto Destinations(params (string Variable, string? Colonne)[] variables) =>
        new("PAIE_CUMULS", "Matricule", variables.Select(v => new PayrollWriteTargetSaveDto(v.Variable, v.Colonne)).ToList());

    private static List<PayrollVariableSaveDto> Saisie(params (string Variable, bool Actif, string? Code, string? Unite)[] changes) =>
        PayrollVariableSelection.Toutes.Select(nom =>
        {
            var change = changes.FirstOrDefault(c => c.Variable == nom);
            return change.Variable is null
                ? new PayrollVariableSaveDto(nom, PayrollVariableSelection.Default.EstActive(nom), null, null, null)
                : new PayrollVariableSaveDto(nom, change.Actif, change.Code, change.Unite, null);
        }).ToList();

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Maintenant;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
