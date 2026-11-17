using System.Text.Json;

using Moq;

using SoftTime.Api.Controllers;
using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.History;
using SoftTime.Infrastructure.Services.Payroll;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>
/// C2c : option par base « remettre à 0 les salariés sans valeur à l'envoi ». Cochée (défaut, y compris sans ligne de
/// configuration) : comportement de C2b. Décochée : l'envoi n'ajoute aucune ligne synthétique, donc aucun zéro écrit,
/// aucun salarié « remis à 0 », ni message ni avertissement s'y rapportant ; les salariés avec valeurs sont écrits comme
/// avant. Le refus d'une période sans aucune ligne calculée ne change pas.
/// </summary>
public class PayrollResetOptionTests
{
    private static readonly DateTime Debut = new(2026, 8, 3);
    private static readonly DateTime Fin = new(2026, 8, 9);

    private static PayrollVariableSelection Option(bool remiseAZero) =>
        PayrollVariableSelection.Default with { RemiseAZeroSansValeur = remiseAZero };

    /// <summary>Écrit réellement par le writer STANDARD (SAGE simulé) : les nombres et le message sont ceux de la production.</summary>
    private sealed class StandardTestWriter(ISagePayrollWriter sage, IPayrollVariableService? variables)
        : StandardPayrollWriter(sage, null!, null!, null!, new NullAudit(), variables)
    {
        protected override Task<SagePayrollTarget> GetInputAsync(CancellationToken ct)
            => Task.FromResult(new SagePayrollTarget("test",
                Enumerable.Range(1, 7).Select(i => new ConstanteSage { CodeConstante = $"HS0{i}", CodeOperande1 = (short)i }).ToArray(),
                new OvertimeSageCodes()));
    }

    private sealed class NullAudit : IAuditTrail
    {
        public Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Writer AUTRE simulé : capture les lignes reçues.</summary>
    private sealed class CapturingOtherWriter : IPayrollWriter
    {
        public List<HsExoDto> Recues { get; private set; } = new();
        public int Appels { get; private set; }
        public bool CanHandle(BddSage database) => true;
        public Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
            => Task.FromResult(new PayrollWriteResultDto("SAGE_TEST", "AUTRE", 0, 0, "ok"));

        public Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
        {
            Appels++;
            Recues = rows.ToList();
            return Task.FromResult(new PayrollWriteResultDto("SAGE_TEST", "AUTRE", rows.Count(r => !r.SansValeur), 0, "ok")
            {
                EmployeesReset = rows.Count(r => r.SansValeur)
            });
        }
    }

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public CapturingSagePayrollWriter Sage { get; } = new();
        public CapturingOtherWriter Autre { get; } = new();
        public OvertimeServices Service { get; }

        /// <param name="option">Option de la base ; null : aucun service de variables (comportement d'avant C2c).</param>
        public Contexte(bool? option, string typeBase = "STANDARD")
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST", TypeBase = typeBase });
            Carte(1, "04634", "BR704");
            Carte(2, "04700", "BR704");
            Source.Employees.AddRange([
                new ExternalEmployee("04634", 101, "N", "P", null, false),
                new ExternalEmployee("04700", 102, "N", "P", null, false)]);
            var variables = option is { } o ? new FixedVariableService(Option(o)) : null;
            IPayrollWriter writer = typeBase == "AUTRE" ? Autre : new StandardTestWriter(Sage, variables);
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            Service = new OvertimeServices(Uow, tenant, Source, new PayrollWriterResolver([writer]),
                new HistoryArchiveService(Uow), new SageLeaveCalendar(tenant, Source), variables: variables);
        }

        public void Carte(int id, string matricule, string departement)
            => Uow.Get<CardPaie>().Items.Add(new CardPaie { Id = id, SageMatricule = matricule, SageBdd = "SAGE_TEST", PointeuseBdd = "PTE_TEST", Departement = departement });

        public void Ligne(string matricule, int numSal, decimal nuit)
            => Uow.Get<HsExoImp>().Items.Add(new HsExoImp { Oid = numSal, Matricule = matricule, NumSal = numSal, Nuit = nuit, PeriodeDebut = Debut, PeriodeFin = Fin, BddSage = "SAGE_TEST" });

        public Task<PayrollWriteResultDto> Envoyer(string? departement = "BR704")
            => Service.SyncPayrollAsync(new PeriodRequest(null, null, Debut, Fin, departement));
    }

    // ---------- Option cochée (défaut) : comportement de C2b ----------

    [Theory]
    [InlineData(null)]   // aucun service de variables
    [InlineData(true)]   // option cochée
    public async Task StandardSend_OptionCheckedOrAbsent_ResetsEmployeesWithoutValue_AsBefore(bool? option)
    {
        var c = new Contexte(option);
        c.Ligne("04634", 101, 9m);

        var resultat = await c.Envoyer();

        c.Sage.Calls.Where(x => x.NumSal == 102).Should().Equal(Enumerable.Range(1, 7).Select(op => (102, (short)op, 0m)));
        resultat.EmployeesUpdated.Should().Be(1);
        resultat.EmployeesReset.Should().Be(1);
        resultat.CategoriesUpdated.Should().Be(14);
        resultat.Message.Should().Contain("1 salarié(s) sans valeur sur la période ont été remis à 0 pour les variables envoyées.");
    }

    // ---------- Option décochée ----------

    [Fact]
    public async Task StandardSend_OptionUnchecked_WritesNoZero_ResetsNobody_AndSaysNothingAboutIt()
    {
        var c = new Contexte(option: false);
        c.Ligne("04634", 101, 9m);

        var resultat = await c.Envoyer();

        c.Sage.Calls.Should().OnlyContain(x => x.NumSal == 101, "aucun zéro pour le salarié sans valeur");
        c.Sage.Calls.Should().HaveCount(7);
        c.Sage.Calls.Single(x => x.Operand == 7).Value.Should().Be(9m, "les valeurs écrites sont inchangées");
        resultat.EmployeesUpdated.Should().Be(1);
        resultat.EmployeesReset.Should().Be(0);
        resultat.EmployeesNotReset.Should().Be(0);
        resultat.CategoriesUpdated.Should().Be(7);
        resultat.Message.Should().Be("Synchronisation des heures supplémentaires vers SAGE terminée.");
        resultat.Message.Should().NotContain("remis à 0").And.NotContain("PAS été remis");
    }

    [Fact]
    public async Task StandardSend_OptionUnchecked_UnknownEmployeeInSage_RaisesNoWarning()
    {
        var c = new Contexte(option: false);
        c.Source.Employees.RemoveAll(e => e.Matricule == "04700");
        c.Ligne("04634", 101, 9m);

        var resultat = await c.Envoyer();

        resultat.EmployeesNotReset.Should().Be(0, "rien n'était demandé : pas d'avertissement « non remis à 0 »");
        resultat.Message.Should().NotContain("PAS été remis");
    }

    [Fact]
    public async Task StandardSend_OptionChecked_UnknownEmployeeInSage_StillWarns()
    {
        var c = new Contexte(option: true);
        c.Source.Employees.RemoveAll(e => e.Matricule == "04700");
        c.Ligne("04634", 101, 9m);

        var resultat = await c.Envoyer();

        resultat.EmployeesNotReset.Should().Be(1);
        resultat.Message.Should().Contain("Attention : 1 salarié(s) sans valeur n'ont PAS été remis à 0");
    }

    [Fact]
    public async Task StandardSend_OptionUnchecked_UncheckedVariableIsStillNeverWritten()
    {
        var c = new Contexte(option: false);
        c.Ligne("04634", 101, 9m);

        await c.Envoyer();

        c.Sage.Calls.Select(x => x.Operand).Should().Equal(Enumerable.Range(1, 7).Select(i => (short)i), "Retard et Absence restent non envoyées");
    }

    // ---------- Refus d'une période sans ligne : identique dans les deux cas ----------

    [Theory]
    [InlineData(true, "STANDARD")]
    [InlineData(false, "STANDARD")]
    [InlineData(true, "AUTRE")]
    [InlineData(false, "AUTRE")]
    public async Task PeriodWithoutAnyRow_IsRefused_WhateverTheOption(bool option, string typeBase)
    {
        var c = new Contexte(option, typeBase);

        var act = () => c.Envoyer();

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Aucune heure calculée sur cette période pour cette sélection : lancez le calcul avant d'envoyer à la paie.");
        c.Sage.Calls.Should().BeEmpty();
        c.Autre.Appels.Should().Be(0);
    }

    // ---------- Autre base ----------

    [Fact]
    public async Task OtherBase_OptionChecked_SendsTheZeroRows()
    {
        var c = new Contexte(option: true, typeBase: "AUTRE");
        c.Ligne("04634", 101, 9m);

        await c.Envoyer();

        c.Autre.Recues.Should().HaveCount(2).And.ContainSingle(r => r.SansValeur && r.Matricule == "04700");
    }

    [Fact]
    public async Task OtherBase_OptionUnchecked_SendsOnlyTheEmployeesWithValues()
    {
        var c = new Contexte(option: false, typeBase: "AUTRE");
        c.Ligne("04634", 101, 9m);

        var resultat = await c.Envoyer();

        c.Autre.Recues.Should().ContainSingle().Which.Should().Match<HsExoDto>(r => r.Matricule == "04634" && !r.SansValeur && r.Nuit == 9m);
        resultat.EmployeesReset.Should().Be(0);
    }

    // ---------- Contrôleur : GET / PUT ----------

    private static JsonElement Json(string texte) => JsonDocument.Parse(texte).RootElement;

    private static readonly PayrollVariablesConfigDto Config =
        new("SAGE_TEST", "STANDARD", true, false, null, [], []) { RemiseAZeroSansValeur = false };

    [Fact]
    public async Task Put_LegacyArrayBody_IsAccepted_AndLeavesTheOptionUnchanged()
    {
        var svc = new Mock<IPayrollVariableService>();
        svc.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyList<PayrollVariableSaveDto>>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Config);

        await new PayrollVariablesController(svc.Object).SaveAsync(Json("""[{"variable":"Nuit","actif":false}]"""), default);

        svc.Verify(s => s.SaveAsync(
            It.Is<IReadOnlyList<PayrollVariableSaveDto>>(l => l.Count == 1 && l[0].Variable == "Nuit" && !l[0].Actif),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{"variables":[{"variable":"Nuit","actif":true}],"remiseAZeroSansValeur":false}""", false)]
    [InlineData("""{"variables":[{"variable":"Nuit","actif":true}],"remiseAZeroSansValeur":true}""", true)]
    public async Task Put_ObjectBody_CarriesTheVariablesAndTheOptionInOneCall(string corps, bool attendu)
    {
        var svc = new Mock<IPayrollVariableService>();
        svc.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyList<PayrollVariableSaveDto>>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Config);

        await new PayrollVariablesController(svc.Object).SaveAsync(Json(corps), default);

        svc.Verify(s => s.SaveAsync(It.Is<IReadOnlyList<PayrollVariableSaveDto>>(l => l.Count == 1), attendu, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{"variables":[]}""")]                                      // champ absent
    [InlineData("""{"variables":[],"remiseAZeroSansValeur":null}""")]         // null explicite
    public async Task Put_ObjectBody_OptionAbsentOrNull_MeansUnchanged(string corps)
    {
        var svc = new Mock<IPayrollVariableService>();
        svc.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyList<PayrollVariableSaveDto>>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Config);

        await new PayrollVariablesController(svc.Object).SaveAsync(Json(corps), default);

        svc.Verify(s => s.SaveAsync(It.IsAny<IReadOnlyList<PayrollVariableSaveDto>>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("\"texte\"")]
    [InlineData("12")]
    [InlineData("""{"variables":"pas un tableau"}""")]
    [InlineData("""{"variables":[],"remiseAZeroSansValeur":"oui"}""")]
    public void Put_UnreadableBody_IsRefusedWithAFrenchMessage(string corps)
    {
        var act = () => PayrollVariablesController.LireCorps(Json(corps));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task Get_ReturnsTheOptionOfTheConfiguration()
    {
        var svc = new Mock<IPayrollVariableService>();
        svc.Setup(s => s.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Config);

        var reponse = await new PayrollVariablesController(svc.Object).GetAsync(default);

        var json = JsonSerializer.Serialize(((Microsoft.AspNetCore.Mvc.ObjectResult)reponse).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("\"remiseAZeroSansValeur\":false");
    }

    [Fact]
    public void ConfigurationDto_DefaultsTheOptionToChecked()
    {
        new PayrollVariablesConfigDto("B", "STANDARD", true, false, null, [], []).RemiseAZeroSansValeur.Should().BeTrue();
        new PayrollVariablesActiveDto("B", []).RemiseAZeroSansValeur.Should().BeTrue();
    }
}
