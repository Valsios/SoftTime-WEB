using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Extras;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Services;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Extras;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Lot D6 : code de la constante SAGE de la cantine configurable par base (T_PAIE_OPTION, clé CodeConstanteCantine) ;
/// validation à l'enregistrement (non vide, longueur de T_CST.CodeConstante, constante existante avec opérande) ; activation
/// de la cantine refusée sans code valide ; comportement par défaut inchangé (CANTINE_CODE).
/// </summary>
public class PayrollCanteenCodeD6Tests
{
    private const string Base = "SFOI_PAIE2025";
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    private sealed class Ctx
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public Mock<IDatabaseConfigurationAuthorizer> Autorisation { get; } = new();
        public Mock<IClosingChecklistCache> Checklist { get; } = new();
        public FakePayrollTargetSchema Schema { get; } = new();
        public List<ExternalConstant> Constantes { get; } = [new("HA06", 42, "Frais"), new("CANT01", 41, "Cantine"), new("CANT02", null, "Sans opérande")];
        public string TypeBase { get; init; } = "STANDARD";

        public Ctx()
        {
            Schema.Tables["T_CST"] = [new("CodeConstante", "nvarchar", null, null) { Longueur = 10 }, new("Intitule", "nvarchar", null, null) { Longueur = 60 }];
        }

        public PayrollVariableService Sut()
        {
            var tenant = new Mock<ITenantConnectionService>();
            tenant.SetupGet(t => t.SageDb).Returns(Base);
            tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { NomBd = Base, TypeBase = TypeBase });
            var codes = new Mock<ICodeConstanteService>();
            codes.Setup(c => c.GetOvertimeSageCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new OvertimeSageCodes());
            codes.Setup(c => c.ListSageConstantOptionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var sage = new Mock<IExternalSourceService>();
            sage.Setup(s => s.GetConstantsAsync(It.IsAny<BddSage>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Constantes.ToList());
            var horloge = new Mock<TimeProvider>();
            horloge.Setup(h => h.GetUtcNow()).Returns(Maintenant);
            horloge.Setup(h => h.LocalTimeZone).Returns(TimeZoneInfo.Utc);
            return new PayrollVariableService(Uow, tenant.Object, Autorisation.Object, new TestCurrentUser(5, 1), new RecordingSendLog(), codes.Object,
                Mock.Of<IExternalDiscoveryService>(), new PayrollVariableCache(), new ClosingAnalysisCache(), Checklist.Object, horloge.Object,
                NullLogger<PayrollVariableService>.Instance, Schema, sage.Object);
        }

        public IReadOnlyList<(string Cle, string Valeur, string? Par)> Options() =>
            Uow.Get<PaieOption>().Items.OrderBy(o => o.Cle).Select(o => (o.Cle, o.Valeur, o.ModifiePar)).ToList();
    }

    private const string Conseil = "Créez d'abord la constante dans SAGE, 10 caractères au plus, puis saisissez son code.";

    // ---------- Comportement par défaut ----------

    [Fact]
    public async Task Configuration_WithoutSetting_ShowsTheHistoricalCode()
    {
        var c = new Ctx();

        var d = (await c.Sut().GetConfigurationAsync()).Domaines;

        d.Should().Be(new PayrollDomainsDto(false, false, "CANTINE_CODE", CodeCantineParDefaut: true));
        PayrollOptions.CodeCantine([]).Should().Be("CANTINE_CODE");
        CanteenService.CodeConstanteCantine.Should().Be("CANTINE_CODE");
    }

    [Fact]
    public async Task CanteenSync_UsesTheHistoricalCodeByDefault_AndTheConfiguredCodeOtherwise()
    {
        var uow = new InMemoryUnitOfWork();
        var tenant = new Mock<ITenantConnectionService>();
        tenant.SetupGet(t => t.SageDb).Returns(Base);
        var sync = new Mock<IPayrollCumulSync>();
        var periode = new PeriodRequest(null, null, new DateTime(2026, 8, 17), new DateTime(2026, 8, 23));
        var sut = new CanteenService(uow, tenant.Object, Mock.Of<IPunchAggregate>(), sync.Object);

        await sut.SyncCanteenAsync(periode);
        uow.Get<PaieOption>().Items.Add(new PaieOption { Id = 1, BddSage = "AUTRE_BASE", Cle = PayrollOptions.CodeConstanteCantine, Valeur = "XX01" });
        await sut.SyncCanteenAsync(periode);
        uow.Get<PaieOption>().Items.Add(new PaieOption { Id = 2, BddSage = Base, Cle = PayrollOptions.CodeConstanteCantine, Valeur = " CANT01 " });
        await sut.SyncCanteenAsync(periode);

        sync.Verify(s => s.SyncExtrasAsync(periode, "CANTINE_CODE", true, It.IsAny<CancellationToken>()), Times.Exactly(2), "défaut, puis réglage d'une autre base ignoré");
        sync.Verify(s => s.SyncExtrasAsync(periode, "CANT01", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Validation du code ----------

    [Fact]
    public async Task SaveCode_Valid_IsStoredTrimmed_WithItsAuthor_AndCachesAreEmptied()
    {
        var c = new Ctx();

        var d = (await c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, " CANT01 "))).Domaines;

        d.Should().Be(new PayrollDomainsDto(false, false, "CANT01", CodeCantineParDefaut: false));
        c.Options().Should().Equal(("CodeConstanteCantine", "CANT01", "u5"));
        c.Checklist.Verify(x => x.Vider(), Times.Once);
        c.Autorisation.Verify(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "Saisissez le code de constante de la cantine.")]
    [InlineData("   ", "Saisissez le code de constante de la cantine.")]
    [InlineData("CANTINE_C01", "Code trop long : 10 caractères au maximum.")]
    [InlineData("XX99", "Ce code n'existe pas dans la paie de cette base.")]
    [InlineData("CANT02", "Ce code n'existe pas dans la paie de cette base.")]
    public async Task SaveCode_Invalid_IsRefused_AndNothingIsWritten(string code, string message)
    {
        var c = new Ctx();

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, true, code)))
            .Should().ThrowAsync<DomainException>().WithMessage(message);

        c.Options().Should().BeEmpty("ni le code ni l'activation des frais ne sont enregistrés");
    }

    [Fact]
    public async Task SaveCode_LengthComesFromTheSchemaOfTheActiveBase()
    {
        var c = new Ctx();
        c.Schema.Tables["T_CST"] = [new("CodeConstante", "nvarchar", null, null) { Longueur = 4 }];
        c.Constantes.Add(new ExternalConstant("CANTI", 43, "Cantine 5"));

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "CANTI")))
            .Should().ThrowAsync<DomainException>().WithMessage("Code trop long : 4 caractères au maximum.");
    }

    [Fact]
    public async Task SaveCode_SchemaUnreadable_IsRefusedWithAClearMessage()
    {
        var c = new Ctx();
        c.Schema.Illisible = true;

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "CANT01")))
            .Should().ThrowAsync<DomainException>().WithMessage("Le schéma de la base SAGE n'a pas pu être lu*");
        c.Options().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCode_Unchanged_IsNotRevalidated()
    {
        var c = new Ctx();
        await c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "CANT01"));
        c.Constantes.RemoveAll(x => x.Code == "CANT01");

        var d = (await c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, true, "CANT01"))).Domaines;

        (d.Frais, d.CodeCantine).Should().Be((true, "CANT01"));
    }

    // ---------- Activation de la cantine ----------

    [Fact]
    public async Task ActivateCanteen_WithTheHistoricalCode_IsRefusedWithAdvice()
    {
        var c = new Ctx();

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(true, false)))
            .Should().ThrowAsync<DomainException>()
            .WithMessage($"Activation de la cantine impossible avec le code CANTINE_CODE : code trop long : 10 caractères au maximum. {Conseil}");
        c.Options().Should().BeEmpty();
    }

    [Fact]
    public async Task ActivateCanteen_WithAConfiguredCodeThatNoLongerExists_IsRefused()
    {
        var c = new Ctx();
        await c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "CANT01"));
        c.Constantes.RemoveAll(x => x.Code == "CANT01");

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(true, false)))
            .Should().ThrowAsync<DomainException>()
            .WithMessage($"Activation de la cantine impossible avec le code CANT01 : ce code n'existe pas dans la paie de cette base. {Conseil}");
    }

    [Fact]
    public async Task ActivateCanteen_WithAValidCode_InOneSave()
    {
        var c = new Ctx();

        var d = (await c.Sut().SaveDomainsAsync(new PayrollDomainsDto(true, false, "cant01"))).Domaines;

        (d.Cantine, d.CodeCantine).Should().Be((true, "cant01"));
        c.Options().Should().Equal(("CodeConstanteCantine", "cant01", "u5"), ("DomaineCantineActif", "1", "u5"));
    }

    [Fact]
    public async Task ActivateCanteen_WithAnInvalidNewCode_GivesTheCodeMessage()
    {
        var c = new Ctx();

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(true, false, "XX99")))
            .Should().ThrowAsync<DomainException>().WithMessage("Ce code n'existe pas dans la paie de cette base.");
    }

    // ---------- Base AUTRE et droits ----------

    [Fact]
    public async Task OtherBase_CodeIsNotConfigurable_AndNotValidated()
    {
        var c = new Ctx { TypeBase = "AUTRE" };

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "N'IMPORTE")))
            .Should().ThrowAsync<DomainException>().WithMessage("La cantine ne s'envoie qu'à une base SAGE STANDARD*");
        var d = (await c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "CANTINE_CODE"))).Domaines;
        d.CodeCantine.Should().Be("CANTINE_CODE", "renvoyer le code actuel n'est pas un changement");
        c.Options().Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutTheConfigurationRight_NothingIsWritten()
    {
        var c = new Ctx();
        c.Autorisation.Setup(a => a.EnsureCanConfigureAsync(It.IsAny<DatabaseKind>(), It.IsAny<DatabaseConfigAction>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Action non autorisée."));

        await FluentActions.Awaiting(() => c.Sut().SaveDomainsAsync(new PayrollDomainsDto(false, false, "CANT01")))
            .Should().ThrowAsync<ForbiddenException>();
        c.Options().Should().BeEmpty();
    }

    [Fact]
    public void ClosureSnapshot_KeepsTheCodeActuallyUsed_InFormat2()
    {
        var ligne = new PayrollSnapshotLine("04634", 103, PayrollDomains.Cantine, "CANT01", PayrollDomains.UniteNombre, 5m, false);
        PayrollSnapshotFingerprint.Canonique(2, PayrollDomains.Cantine, Base, new DateTime(2026, 8, 17), new DateTime(2026, 8, 23), "STANDARD", [ligne])
            .Should().EndWith("\n04634|103|CANTINE|CANT01|N|5.0000|0");
    }
}
