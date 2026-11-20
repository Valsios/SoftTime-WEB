using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>Configuration des variables envoyées à la paie : défauts, portée par base, refus, journal, caches.</summary>
public class PayrollVariableServiceTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    // ---------- Règles pures ----------

    [Fact]
    public void Default_ReproducesTheHistoricalBehaviour()
    {
        PayrollVariableSelection.Default.HeuresSupActives.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit");
        PayrollVariableSelection.Default.EstActive("Retard").Should().BeFalse();
        PayrollVariableSelection.Default.EstActive("Absence").Should().BeFalse();
        PayrollVariableRules.TypesSansObjet(PayrollVariableSelection.Default).Should().BeEmpty();
        PayrollVariableSelection.HeuresSup.Should().Equal(PayrollWritingConfiguration.Categories);
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-30", true)]
    [InlineData("2026-10-27", "2026-11-02", true)]   // période à cheval sur le mois en cours
    [InlineData("2026-10-01", "2026-10-31", false)]  // mois précédent : modification permise
    [InlineData("2026-12-01", "2026-12-31", false)]
    public void MoisEnCoursEnvoye_PeriodOverlappingTheCalendarMonth_Blocks(string debut, string fin, bool bloque)
        => PayrollVariableRules.MoisEnCoursEnvoye([(DateTime.Parse(debut), DateTime.Parse(fin))], new DateTime(2026, 11, 16))
            .Should().Be(bloque);

    [Fact]
    public void Valider_RetardActiveWithoutCode_IsRefusedOnSage_AndWithoutColumnOnOtherBase()
    {
        // Arrange
        var lignes = Lignes(("Retard", true, null, "H", null));

        // Act
        var sage = PayrollVariableRules.Valider(lignes, baseSage: true);
        var autre = PayrollVariableRules.Valider(lignes, baseSage: false);

        // Assert
        sage.Should().ContainSingle().Which.Should().Contain("Retard ne peut pas être cochée tant que son code de paie n'est pas renseigné");
        autre.Should().ContainSingle().Which.Should().Contain("Retard ne peut pas être cochée tant que sa colonne cible n'est pas renseignée");
        // Adapté au lot C3 (avant : Absence cochée refusée, « prochaine étape ») : Absence cochée avec code et unité est
        // acceptée ; sans code, elle est refusée comme Retard.
        PayrollVariableRules.Valider(Lignes(("Absence", true, "ABS01", "J", null)), baseSage: true).Should().BeEmpty();
        PayrollVariableRules.Valider(Lignes(("Absence", true, null, "J", null)), baseSage: true).Should().ContainSingle()
            .Which.Should().Contain("Absence ne peut pas être cochée tant que son code de paie n'est pas renseigné");
    }

    [Fact]
    public void Valider_MissingOrUnknownVariable_IsRefused()
    {
        var lignes = Lignes().Where(l => l.Variable != "Nuit").Append(new PayrollVariableSetting("Bonus", true, null, null, null)).ToList();

        PayrollVariableRules.Valider(lignes, baseSage: true).Should().Contain(e => e.Contains("Bonus")).And.Contain(e => e.Contains("Nuit est absente"));
    }

    [Fact]
    public void TypesSansObjet_FerieUnchecked_IgnoresHolidayAnomalies_AllExoImpoUnchecked_IgnoresAtypicalOvertime()
    {
        PayrollVariableRules.TypesSansObjet(PayrollVariableSelection.Avec(["Exo130", "Dimanche", "Nuit"]))
            .Should().BeEquivalentTo([ClosingAnomalyType.FerieNonApplique]);
        PayrollVariableRules.TypesSansObjet(PayrollVariableSelection.Avec(["Ferie"]))
            .Should().BeEquivalentTo([ClosingAnomalyType.HeuresSupAtypiques]);
    }

    // ---------- Lecture ----------

    [Fact]
    public async Task GetSelection_NoRow_IsTheDefaultSelection()
    {
        var (sut, _, _) = Creer();

        (await sut.GetSelectionAsync("SAGE_TEST")).Should().BeSameAs(PayrollVariableSelection.Default);
    }

    [Fact]
    public async Task GetSelection_OneBaseConfigured_DoesNotInfluenceAnotherBase()
    {
        // Arrange
        var (sut, uow, _) = Creer();
        uow.Get<PaieVariable>().Items.Add(new PaieVariable { BddSage = "SAGE_A", Variable = "Nuit", Actif = false });

        // Act
        var a = await sut.GetSelectionAsync("SAGE_A");
        var b = await sut.GetSelectionAsync("SAGE_B");

        // Assert : variable absente des lignes = sa valeur par défaut.
        a.HeuresSupActives.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche");
        b.Should().BeSameAs(PayrollVariableSelection.Default);
    }

    // ---------- Enregistrement ----------

    [Fact]
    public async Task Save_ValidConfiguration_WritesNineRowsWithAuthor_AndInvalidatesTheCaches()
    {
        // Arrange
        var (sut, uow, contexte) = Creer();
        (await sut.GetSelectionAsync("SAGE_TEST")).Should().BeSameAs(PayrollVariableSelection.Default); // mis en cache
        var cle = new ClosingChecklistRequest(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null);
        contexte.Checklist.Set("cle", new ClosingChecklistDto(cle.From, cle.To, null, []), Maintenant, TimeSpan.FromMinutes(5));

        // Act
        var resultat = await sut.SaveAsync(Saisie(("Nuit", false, null, null, null)));

        // Assert
        uow.Get<PaieVariable>().Items.Should().HaveCount(9).And.AllSatisfy(v =>
        {
            v.BddSage.Should().Be("SAGE_TEST");
            v.ModifiePar.Should().Be("u1");
            v.ModifieLe.Should().Be(Maintenant.UtcDateTime);
        });
        resultat.Variables.Single(v => v.Variable == "Nuit").Actif.Should().BeFalse();
        resultat.ParDefaut.Should().BeFalse();
        (await sut.GetSelectionAsync("SAGE_TEST")).EstActive("Nuit").Should().BeFalse("le cache de la base est invalidé");
        contexte.Checklist.TryGet("cle", Maintenant, out _).Should().BeFalse("le cache du Contrôle avant paie est vidé");
    }

    [Fact]
    public async Task Save_UnchangedRows_AreNotRewritten()
    {
        // Arrange
        var (sut, uow, _) = Creer();
        await sut.SaveAsync(Saisie());
        var premiere = uow.Get<PaieVariable>().Items.Single(v => v.Variable == "Exo130");
        premiere.ModifiePar = "ancien";

        // Act
        await sut.SaveAsync(Saisie(("Nuit", false, null, null, null)));

        // Assert
        premiere.ModifiePar.Should().Be("ancien");
        uow.Get<PaieVariable>().Items.Single(v => v.Variable == "Nuit").ModifiePar.Should().Be("u1");
    }

    [Fact]
    public async Task Save_CurrentMonthAlreadySent_IsRefusedAndChangesNothing()
    {
        // Arrange
        var (sut, uow, contexte) = Creer();
        contexte.Envois.Periodes.Add((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));

        // Act
        var act = () => sut.SaveAsync(Saisie(("Nuit", false, null, null, null)));

        // Assert
        (await act.Should().ThrowExactlyAsync<ConflictException>()).Which.Message.Should().StartWith("Mois déjà envoyé en paie (période 2026-11-01 → 2026-11-30) : modification impossible.");
        uow.Get<PaieVariable>().Items.Should().BeEmpty();
        (await sut.GetConfigurationAsync()).MoisEnvoye.Should().BeTrue();
    }

    [Fact]
    public async Task Save_PreviousMonthSent_IsAllowed()
    {
        // Arrange
        var (sut, uow, contexte) = Creer();
        contexte.Envois.Periodes.Add((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));

        // Act
        await sut.SaveAsync(Saisie(("Nuit", false, null, null, null)));

        // Assert
        uow.Get<PaieVariable>().Items.Should().HaveCount(9);
    }

    [Fact]
    public async Task Save_RetardCheckedWithoutCode_IsRefused()
    {
        // Arrange
        var (sut, uow, _) = Creer();

        // Act
        var act = () => sut.SaveAsync(Saisie(("Retard", true, null, "H", null)));

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("code de paie");
        uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_WithoutTheRightToModifyTheBase_IsForbidden()
    {
        // Arrange
        var (sut, uow, contexte) = Creer();
        contexte.Autorisation.Setup(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "SAGE_TEST", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("refus"));

        // Act
        var lecture = () => sut.GetConfigurationAsync();
        var ecriture = () => sut.SaveAsync(Saisie());

        // Assert
        await lecture.Should().ThrowExactlyAsync<ForbiddenException>();
        await ecriture.Should().ThrowExactlyAsync<ForbiddenException>();
        uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConfiguration_SageBase_ShowsOvertimeCodesReadOnly()
    {
        var (sut, _, _) = Creer();

        var config = await sut.GetConfigurationAsync();

        config.ParDefaut.Should().BeTrue();
        config.TypeBase.Should().Be("STANDARD");
        config.Variables.Select(v => v.Variable).Should().Equal(PayrollVariableSelection.Toutes);
        config.Variables.Take(7).Select(v => v.CibleExistante).Should().Equal("HS01", "HS02", "HS03", "HS04", "HS05", "HS06", "HS07");
        // Adapté au lot C2 (avant : Retard et Absence « sans envoi ») : Retard est envoyable, Absence pas encore.
        config.Variables.Skip(7).Should().AllSatisfy(v => v.Actif.Should().BeFalse());
        // Adapté au lot C3 : Absence est envoyable (avant : sans envoi).
        config.Variables.Skip(7).Select(v => v.SansEnvoi).Should().Equal(false, false);
    }

    // ---------- Journal central ----------

    [Fact]
    public void Journal_ModifiedVariable_RecordsWhoBeforeAndAfter()
    {
        // Arrange
        using var db = new SoftTimeDbContext(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimePayrollVariablesJournal;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        var variable = new PaieVariable { Id = 4, BddSage = "SAGE_TEST", Variable = "Nuit", Actif = true };
        db.Attach(variable);
        variable.Actif = false;
        db.Entry(variable).Property(v => v.Actif).IsModified = true;

        // Act
        new AuditJournalSaveChangesInterceptor(new TestCurrentUser(1, 1), Mock.Of<ICompanyContext>(), Options.Create(new AuditJournalOptions()))
            .CaptureChanges(db);

        // Assert
        var entree = db.ChangeTracker.Entries<JournalAudit>().Select(e => e.Entity).Should().ContainSingle().Which;
        entree.Tables.Should().Be("T_PAIE_VARIABLE");
        entree.Colonnes.Should().Be("ACTIF");
        entree.Anciens.Should().Be("True");
        entree.Nouveaux.Should().Be("False");
        entree.Users.Should().Be("u1");
        entree.DateUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ---------- Marqueur T_PAIE_ENVOI ----------

    [Fact]
    public async Task SendLog_RecordCompleteAndList_UsesItsOwnScope()
    {
        // Arrange
        var uow = new InMemoryUnitOfWork();
        var sut = new PayrollSendLog(new ScopeFactory(uow), NullLogger<PayrollSendLog>.Instance);

        // Act
        var id = await sut.RecordAsync(new PayrollSend("SAGE_TEST", "STANDARD", new DateTime(2026, 10, 1, 8, 0, 0), new DateTime(2026, 10, 31), null, "EN_COURS", 3));
        await sut.CompleteAsync(id, "SUCCES", 14);
        await sut.RecordAsync(new PayrollSend("SAGE_AUTRE", "AUTRE", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), "rh", "SUCCES", 7));

        // Assert
        var envoi = uow.Get<PaieEnvoi>().Items.First();
        envoi.Statut.Should().Be("SUCCES");
        envoi.Lignes.Should().Be(14);
        envoi.PeriodeDebut.Should().Be(new DateTime(2026, 10, 1));
        envoi.EnvoyePar.Should().Be("SYSTEME");
        (await sut.ListPeriodsAsync("SAGE_TEST")).Should().Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
    }

    // ---------- C2c : option « remettre à 0 les salariés sans valeur à l'envoi » ----------

    [Fact]
    public async Task Option_NoRow_IsCheckedByDefault_AndTheDefaultSelectionIsKept()
    {
        var (sut, uow, _) = Creer();

        (await sut.GetConfigurationAsync()).RemiseAZeroSansValeur.Should().BeTrue();
        (await sut.GetSelectionAsync("SAGE_TEST")).Should().BeSameAs(PayrollVariableSelection.Default);
        (await sut.GetSelectionAsync("SAGE_TEST")).RemiseAZeroSansValeur.Should().BeTrue();
        (await sut.GetActivesAsync()).RemiseAZeroSansValeur.Should().BeTrue();
        uow.Get<PaieOption>().Items.Should().BeEmpty("une lecture n'écrit rien");
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("n'importe quoi", true)]   // valeur illisible : la valeur par défaut
    [InlineData("", true)]
    public void Option_StoredValue_IsReadWithTheDefaultWhenUnreadable(string valeur, bool attendu)
    {
        var lignes = new[] { new PaieOption { BddSage = "SAGE_TEST", Cle = PayrollOptions.RemiseAZeroSansValeur, Valeur = valeur } };

        PayrollOptions.Booleen(lignes, PayrollOptions.RemiseAZeroSansValeur, parDefaut: true).Should().Be(attendu);
        PayrollVariableService.Selection([], lignes).RemiseAZeroSansValeur.Should().Be(attendu);
    }

    [Fact]
    public async Task Save_OptionUnchecked_CreatesTheRowWithAuthor_AndInvalidatesTheCaches()
    {
        // Arrange
        var (sut, uow, contexte) = Creer();
        (await sut.GetSelectionAsync("SAGE_TEST")).RemiseAZeroSansValeur.Should().BeTrue(); // mis en cache
        var cle = new ClosingChecklistRequest(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null);
        contexte.Checklist.Set("cle", new ClosingChecklistDto(cle.From, cle.To, null, []), Maintenant, TimeSpan.FromMinutes(5));

        // Act
        var resultat = await sut.SaveAsync(Saisie(), remiseAZeroSansValeur: false);

        // Assert
        var option = uow.Get<PaieOption>().Items.Should().ContainSingle().Which;
        option.Should().Match<PaieOption>(o => o.BddSage == "SAGE_TEST" && o.Cle == "RemiseAZeroSansValeur" && o.Valeur == "0"
            && o.ModifiePar == "u1" && o.ModifieLe == Maintenant.UtcDateTime);
        resultat.RemiseAZeroSansValeur.Should().BeFalse("la réponse renvoie l'option enregistrée");
        (await sut.GetConfigurationAsync()).RemiseAZeroSansValeur.Should().BeFalse("valeur enregistrée relue");
        (await sut.GetSelectionAsync("SAGE_TEST")).RemiseAZeroSansValeur.Should().BeFalse("le cache de la base est invalidé");
        (await sut.GetActivesAsync()).RemiseAZeroSansValeur.Should().BeFalse();
        contexte.Checklist.TryGet("cle", Maintenant, out _).Should().BeFalse("le cache du Contrôle avant paie est vidé");
        uow.Get<PaieVariable>().Items.Should().HaveCount(9, "les variables partent dans le même enregistrement");
    }

    [Fact]
    public async Task Save_OptionRechecked_UpdatesTheSameRow_NeverDeletes()
    {
        var (sut, uow, _) = Creer();
        await sut.SaveAsync(Saisie(), remiseAZeroSansValeur: false);
        uow.Get<PaieOption>().Items.Single().ModifiePar = "ancien";

        await sut.SaveAsync(Saisie(), remiseAZeroSansValeur: true);

        var option = uow.Get<PaieOption>().Items.Should().ContainSingle().Which;
        option.Valeur.Should().Be("1");
        option.ModifiePar.Should().Be("u1");
        (await sut.GetSelectionAsync("SAGE_TEST")).RemiseAZeroSansValeur.Should().BeTrue();
    }

    [Fact]
    public async Task Save_OptionChecked_WithoutAnyRow_CreatesNothing()
    {
        var (sut, uow, _) = Creer();

        await sut.SaveAsync(Saisie(), remiseAZeroSansValeur: true);

        uow.Get<PaieOption>().Items.Should().BeEmpty("la valeur par défaut n'a pas besoin de ligne");
    }

    [Fact]
    public async Task Save_OptionAbsentFromTheRequest_LeavesItUnchanged()
    {
        var (sut, uow, _) = Creer();
        await sut.SaveAsync(Saisie(), remiseAZeroSansValeur: false);
        var option = uow.Get<PaieOption>().Items.Single();
        option.ModifiePar = "ancien";

        await sut.SaveAsync(Saisie(("Nuit", false, null, null, null)));          // champ absent (null)

        option.Valeur.Should().Be("0");
        option.ModifiePar.Should().Be("ancien", "ligne non réécrite");
        (await sut.GetConfigurationAsync()).RemiseAZeroSansValeur.Should().BeFalse();
        uow.Get<PaieVariable>().Items.Single(v => v.Variable == "Nuit").Actif.Should().BeFalse("les variables restent enregistrées");
    }

    [Fact]
    public async Task Save_OptionOnlyChange_LeavesTheVariablesAsTheyWere()
    {
        var (sut, uow, _) = Creer();
        await sut.SaveAsync(Saisie(("Nuit", false, null, null, null)));
        var avant = uow.Get<PaieVariable>().Items.Select(v => (v.Variable, v.Actif, v.ModifieLe, v.ModifiePar)).ToList();

        await sut.SaveAsync(Saisie(("Nuit", false, null, null, null)), remiseAZeroSansValeur: false);

        uow.Get<PaieVariable>().Items.Select(v => (v.Variable, v.Actif, v.ModifieLe, v.ModifiePar)).Should().Equal(avant);
        uow.Get<PaieOption>().Items.Should().ContainSingle().Which.Valeur.Should().Be("0");
    }

    [Fact]
    public async Task Option_IsPerBase_AnotherBaseKeepsTheDefault()
    {
        var (sut, uow, _) = Creer();
        uow.Get<PaieOption>().Items.Add(new PaieOption { BddSage = "SAGE_A", Cle = PayrollOptions.RemiseAZeroSansValeur, Valeur = "0" });

        (await sut.GetSelectionAsync("SAGE_A")).RemiseAZeroSansValeur.Should().BeFalse();
        (await sut.GetSelectionAsync("SAGE_B")).Should().BeSameAs(PayrollVariableSelection.Default);
        (await sut.GetConfigurationAsync()).RemiseAZeroSansValeur.Should().BeTrue("la base active (SAGE_TEST) n'a pas de ligne");
    }

    [Fact]
    public async Task Save_OptionOnOtherBase_IsStoredToo()
    {
        var (sut, uow, _) = Creer("AUTRE");

        await sut.SaveAsync(Saisie(), remiseAZeroSansValeur: false);

        uow.Get<PaieOption>().Items.Should().ContainSingle().Which.Valeur.Should().Be("0");
    }

    [Fact]
    public async Task Save_OptionWhileCurrentMonthSent_IsRefusedWith409_AndWritesNothing()
    {
        var (sut, uow, contexte) = Creer();
        contexte.Envois.Periodes.Add((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));

        var act = () => sut.SaveAsync(Saisie(), remiseAZeroSansValeur: false);

        (await act.Should().ThrowExactlyAsync<ConflictException>()).Which.Message.Should().StartWith("Mois déjà envoyé en paie");
        uow.Get<PaieOption>().Items.Should().BeEmpty();
        uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_OptionWithoutTheRightToModifyTheBase_IsForbidden()
    {
        var (sut, uow, contexte) = Creer();
        contexte.Autorisation.Setup(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "SAGE_TEST", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("refus"));

        var act = () => sut.SaveAsync(Saisie(), remiseAZeroSansValeur: false);

        await act.Should().ThrowExactlyAsync<ForbiddenException>();
        uow.Get<PaieOption>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_InvalidVariables_WithTheOption_WritesNeitherOfThem()
    {
        var (sut, uow, _) = Creer();

        var act = () => sut.SaveAsync(Saisie(("Retard", true, null, "H", null)), remiseAZeroSansValeur: false);

        await act.Should().ThrowExactlyAsync<DomainException>();
        uow.Get<PaieOption>().Items.Should().BeEmpty();
        uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public void Journal_ModifiedOption_RecordsWhoBeforeAndAfter()
    {
        // Arrange
        using var db = new SoftTimeDbContext(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimePayrollVariablesJournal;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        var option = new PaieOption { Id = 2, BddSage = "SAGE_TEST", Cle = PayrollOptions.RemiseAZeroSansValeur, Valeur = "1" };
        db.Attach(option);
        option.Valeur = "0";
        db.Entry(option).Property(o => o.Valeur).IsModified = true;

        // Act
        new AuditJournalSaveChangesInterceptor(new TestCurrentUser(1, 1), Mock.Of<ICompanyContext>(), Options.Create(new AuditJournalOptions()))
            .CaptureChanges(db);

        // Assert
        var entree = db.ChangeTracker.Entries<JournalAudit>().Select(e => e.Entity).Should().ContainSingle().Which;
        entree.Tables.Should().Be("T_PAIE_OPTION");
        entree.Colonnes.Should().Be("VALEUR");
        entree.Anciens.Should().Be("1");
        entree.Nouveaux.Should().Be("0");
        entree.Users.Should().Be("u1");
    }

    // ---------- Aides ----------

    private sealed record Contexte(RecordingSendLog Envois, Mock<IDatabaseConfigurationAuthorizer> Autorisation, ClosingChecklistCache Checklist);

    private static (PayrollVariableService Sut, InMemoryUnitOfWork Uow, Contexte Contexte) Creer(string typeBase = "STANDARD")
    {
        var uow = new InMemoryUnitOfWork();
        var tenant = new Mock<ITenantConnectionService>();
        tenant.SetupGet(t => t.SageDb).Returns("SAGE_TEST");
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { NomBd = "SAGE_TEST", TypeBase = typeBase });
        var codes = new Mock<ICodeConstanteService>();
        codes.Setup(c => c.GetOvertimeSageCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new OvertimeSageCodes());
        var horloge = new HorlogeFixe();
        var contexte = new Contexte(new RecordingSendLog(), new Mock<IDatabaseConfigurationAuthorizer>(), new ClosingChecklistCache());
        var sut = new PayrollVariableService(uow, tenant.Object, contexte.Autorisation.Object, new TestCurrentUser(1, 1), contexte.Envois,
            codes.Object, Mock.Of<IExternalDiscoveryService>(), new PayrollVariableCache(), new ClosingAnalysisCache(), contexte.Checklist,
            horloge, NullLogger<PayrollVariableService>.Instance);
        return (sut, uow, contexte);
    }

    private static List<PayrollVariableSetting> Lignes(params (string Variable, bool Actif, string? Code, string? Unite, string? Colonne)[] changes) =>
        PayrollVariableSelection.Toutes.Select(nom =>
        {
            var change = changes.FirstOrDefault(c => c.Variable == nom);
            return change.Variable is null
                ? new PayrollVariableSetting(nom, PayrollVariableSelection.Default.EstActive(nom), null, null, null)
                : new PayrollVariableSetting(nom, change.Actif, change.Code, change.Unite, change.Colonne);
        }).ToList();

    private static List<PayrollVariableSaveDto> Saisie(params (string Variable, bool Actif, string? Code, string? Unite, string? Colonne)[] changes) =>
        Lignes(changes).Select(l => new PayrollVariableSaveDto(l.Variable, l.Actif, l.CodePaie, l.Unite, l.ColonneCible)).ToList();

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Maintenant;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class ScopeFactory(IUnitOfWork uow) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(uow);

        private sealed class Scope(IUnitOfWork uow) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(IUnitOfWork) ? uow : null;
            public void Dispose() { }
        }
    }
}
