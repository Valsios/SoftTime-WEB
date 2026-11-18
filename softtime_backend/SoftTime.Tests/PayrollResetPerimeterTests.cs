using Moq;

using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.History;
using SoftTime.Infrastructure.Services.Payroll;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>
/// C2b : le service de synchronisation construit le périmètre de l'envoi (cartes de la période, du département et des
/// matricules demandés) et ajoute une ligne « sans valeur » pour chaque salarié du périmètre sans ligne T_HSExoImp.
/// Un salarié hors périmètre n'est pas touché.
/// </summary>
public class PayrollResetPerimeterTests
{
    private static readonly DateTime Debut = new(2026, 8, 3);
    private static readonly DateTime Fin = new(2026, 8, 9);

    private sealed class CapturingWriter : IPayrollWriter
    {
        public List<HsExoDto> Recues { get; private set; } = new();
        public int Appels { get; private set; }
        public PayrollSyncContext? Contexte { get; private set; }
        public bool CanHandle(BddSage database) => true;
        public Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
            => Task.FromResult(new PayrollWriteResultDto("SAGE_TEST", "STANDARD", 0, 0, "ok"));

        public Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
        {
            Appels++;
            Recues = rows.ToList();
            Contexte = auditContext;
            return Task.FromResult(new PayrollWriteResultDto("SAGE_TEST", "STANDARD", rows.Count, 0, "ok"));
        }
    }

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public CapturingWriter Writer { get; } = new();
        public OvertimeServices Service { get; }

        public Contexte(IExternalSourceService? source = null, string typeBase = "STANDARD")
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST", TypeBase = typeBase });
            Carte(1, "04634", "BR704");
            Carte(2, "04700", "BR704");
            Carte(3, "09999", "AUTRE_DEPT");
            Source.Employees.AddRange([
                new ExternalEmployee("04634", 101, "N", "P", null, false),
                new ExternalEmployee("04700", 102, "N", "P", null, false),
                new ExternalEmployee("09999", 103, "N", "P", null, false)]);
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            Service = new OvertimeServices(Uow, tenant, source ?? Source, new PayrollWriterResolver([Writer]),
                new HistoryArchiveService(Uow), new SageLeaveCalendar(tenant, Source));
        }

        public void Carte(int id, string matricule, string departement)
            => Uow.Get<CardPaie>().Items.Add(new CardPaie { Id = id, SageMatricule = matricule, SageBdd = "SAGE_TEST", PointeuseBdd = "PTE_TEST", Departement = departement });

        public void Ligne(string matricule, int numSal, decimal nuit)
            => Uow.Get<HsExoImp>().Items.Add(new HsExoImp { Oid = numSal, Matricule = matricule, NumSal = numSal, Nuit = nuit, PeriodeDebut = Debut, PeriodeFin = Fin, BddSage = "SAGE_TEST" });

        public Task<PayrollWriteResultDto> Envoyer(string? departement = null, string? de = null, string? a = null)
            => Service.SyncPayrollAsync(new PeriodRequest(de, a, Debut, Fin, departement));
    }

    [Fact]
    public async Task EmployeeWithValues_IsSentAsIs_AndEmployeeWithoutValuesGetsASyntheticZeroRow()
    {
        var c = new Contexte();
        c.Ligne("04634", 101, 9m);

        await c.Envoyer("BR704");

        c.Writer.Recues.Should().HaveCount(2);
        c.Writer.Recues.Single(r => !r.SansValeur).Should().Match<HsExoDto>(r => r.Matricule == "04634" && r.Nuit == 9m);
        c.Writer.Recues.Single(r => r.SansValeur).Should().Match<HsExoDto>(r =>
            r.Matricule == "04700" && r.NumSal == 102 && r.Nuit == 0m && r.Exo130 == 0m && r.Retard == 0m);
    }

    [Fact]
    public async Task SendFilter_IsPassedToTheWriter_ForTheSnapshot()
    {
        var c = new Contexte();
        c.Ligne("04634", 101, 9m);

        await c.Envoyer("BR704");
        c.Writer.Contexte!.Filtre.Should().Be(new PayrollSendFilter("BR704", null, null));
        await c.Envoyer(de: "04600", a: "04650");
        c.Writer.Contexte!.Filtre.Should().Be(new PayrollSendFilter(null, "04600", "04650"));
        await c.Envoyer();
        c.Writer.Contexte!.Filtre.AvecFiltre.Should().BeFalse();
    }

    [Fact]
    public async Task EmployeeOutsideTheDepartment_IsNotTouched()
    {
        var c = new Contexte();
        c.Ligne("04634", 101, 9m);

        await c.Envoyer("BR704");

        c.Writer.Recues.Select(r => r.Matricule).Should().BeEquivalentTo("04634", "04700").And.NotContain("09999");
    }

    [Fact]
    public async Task EmployeeOutsideTheMatriculeRange_IsNotTouched()
    {
        var c = new Contexte();
        c.Ligne("04634", 101, 9m);

        await c.Envoyer(de: "04600", a: "04650");

        c.Writer.Recues.Select(r => r.Matricule).Should().Equal("04634");
    }

    [Fact]
    public async Task RowOfAnotherPeriod_DoesNotCountAsAValueRow()
    {
        var c = new Contexte();
        c.Ligne("04700", 102, 4m);
        c.Uow.Get<HsExoImp>().Items.Add(new HsExoImp { Oid = 50, Matricule = "04634", NumSal = 101, Nuit = 5m, PeriodeDebut = Debut.AddDays(-7), PeriodeFin = Fin.AddDays(-7), BddSage = "SAGE_TEST" });

        await c.Envoyer("BR704");

        c.Writer.Recues.Single(r => r.Matricule == "04634").SansValeur.Should().BeTrue();
        c.Writer.Recues.Single(r => r.Matricule == "04700").SansValeur.Should().BeFalse();
    }

    [Fact]
    public async Task EmployeeUnknownInSage_GetsARowWithoutEmployeeNumber()
    {
        var c = new Contexte();
        c.Source.Employees.RemoveAll(e => e.Matricule == "04700");
        c.Ligne("04634", 101, 9m);

        await c.Envoyer("BR704");

        c.Writer.Recues.Single(r => r.Matricule == "04700").NumSal.Should().BeNull();
    }

    [Fact]
    public async Task SageEmployeeReadFails_SendIsNotBlocked_AndZeroRowsCarryNoEmployeeNumber()
    {
        var source = new Mock<IExternalSourceService>();
        source.Setup(s => s.GetEmployeesAsync(It.IsAny<BddSage>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("SAGE injoignable"));
        var c = new Contexte(source.Object);
        c.Ligne("04634", 101, 9m);

        await c.Envoyer("BR704");

        c.Writer.Recues.Single(r => !r.SansValeur).NumSal.Should().Be(101);
        c.Writer.Recues.Single(r => r.SansValeur).NumSal.Should().BeNull();
    }

    [Fact]
    public async Task SageEmployeeListEmpty_ZeroRowsCarryNoEmployeeNumber()
    {
        var c = new Contexte();
        c.Source.Employees.Clear();
        c.Ligne("04634", 101, 9m);

        await c.Envoyer("BR704");

        c.Writer.Recues.Where(r => r.SansValeur).Should().OnlyContain(r => r.NumSal == null).And.NotBeEmpty();
    }

    // ---------- Refus d'une sélection sans aucune heure calculée ----------

    [Theory]
    [InlineData("STANDARD")]
    [InlineData("AUTRE")]
    public async Task PeriodWithoutAnyRow_IsRefused_BeforeAnyWrite(string typeBase)
    {
        var c = new Contexte(typeBase: typeBase);

        var act = () => c.Envoyer("BR704");

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Aucune heure calculée sur cette période pour cette sélection : lancez le calcul avant d'envoyer à la paie.");
        c.Writer.Appels.Should().Be(0, "ni valeur, ni zéro, ni marqueur d'envoi, ni synthèse de succès");
    }

    [Fact]
    public async Task PeriodWithoutAnyRow_IsRefused_ForTheWholeBaseToo()
    {
        var c = new Contexte();

        var act = () => c.Envoyer();

        await act.Should().ThrowAsync<DomainException>().WithMessage(SoftTime.Infrastructure.Services.Overtime.OvertimePayrollSyncService.AucuneHeureCalculee);
        c.Writer.Appels.Should().Be(0);
    }

    [Fact]
    public async Task DepartmentWithoutRow_IsRefused_EvenThoughTheWholeBaseWouldBeAccepted()
    {
        var c = new Contexte();
        c.Ligne("09999", 103, 5m);

        await c.Envoyer();                                  // base entière : une ligne, acceptée
        c.Writer.Appels.Should().Be(1);

        var act = () => c.Envoyer("BR704");                 // département sans ligne : refusé
        await act.Should().ThrowAsync<DomainException>().WithMessage(SoftTime.Infrastructure.Services.Overtime.OvertimePayrollSyncService.AucuneHeureCalculee);
        c.Writer.Appels.Should().Be(1);
    }

    [Fact]
    public async Task MatriculeRangeWithoutRow_IsRefused_EvenThoughThePeriodHasRows()
    {
        var c = new Contexte();
        c.Ligne("04634", 101, 9m);

        var act = () => c.Envoyer(de: "04700", a: "04800");

        await act.Should().ThrowAsync<DomainException>().WithMessage(SoftTime.Infrastructure.Services.Overtime.OvertimePayrollSyncService.AucuneHeureCalculee);
        c.Writer.Appels.Should().Be(0);
    }

    [Fact]
    public async Task PeriodWithAtLeastOneRow_IsAccepted_AndResetsTheOthers()
    {
        var c = new Contexte();
        c.Ligne("04634", 101, 9m);

        var resultat = await c.Envoyer("BR704");

        c.Writer.Appels.Should().Be(1);
        c.Writer.Recues.Should().HaveCount(2).And.ContainSingle(r => r.SansValeur && r.Matricule == "04700");
        resultat.Should().NotBeNull();
    }

    [Fact]
    public void SyntheticFlag_IsNotExposedByTheApi()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new HsExoDto { SansValeur = true });

        json.ToLowerInvariant().Should().NotContain("sansvaleur");
    }
}
