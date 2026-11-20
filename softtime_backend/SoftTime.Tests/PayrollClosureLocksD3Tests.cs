using System.Diagnostics;

using Moq;

using SoftTime.Application.Abstractions.Extras;
using SoftTime.Application.Abstractions.Overtime;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.EmployeePlanning;
using SoftTime.Infrastructure.Services.Extras;
using SoftTime.Infrastructure.Services.History;
using SoftTime.Infrastructure.Services.Overtime;
using SoftTime.Infrastructure.Services.Payroll;
using SoftTime.Infrastructure.Services.PunchProcessing;
using SoftTime.Infrastructure.Services.Punches;

namespace SoftTime.Tests;

/// <summary>
/// Lot D3 : verrous « période clôturée » sur chaque chemin qui modifie une source de la paie. Chaque chemin verrouillé
/// refuse (409, message commun) SANS rien écrire ; une période voisine, une période non clôturée et une autre base
/// restent modifiables ; le contrôle commun reste rapide sur un grand lot.
/// </summary>
public class PayrollClosureLocksD3Tests
{
    private const string Base = "SAGE_TEST";
    private static readonly DateTime Debut = new(2026, 8, 17);
    private static readonly DateTime Fin = new(2026, 8, 23);
    private static readonly DateTime DansLaPeriode = new(2026, 8, 20);
    private static readonly DateTime Voisin = new(2026, 8, 24);
    private const string Message = "Période clôturée (du 17/08/2026 au 23/08/2026) : modification impossible. Les corrections se font sur la période suivante.";

    private sealed class Ctx
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public string BaseActive { get; init; } = Base;

        public Ctx(bool cloturee = true)
        {
            if (cloturee)
                Uow.Get<PaieCloture>().Items.Add(new PaieCloture { Id = 1, BddSage = Base, PeriodeDebut = Debut, PeriodeFin = Fin, CloturePar = "rh", Empreinte = new string('a', 64) });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = true });
        }

        public ITenantConnectionService Tenant()
        {
            var t = new Mock<ITenantConnectionService>();
            t.SetupGet(x => x.SageDb).Returns(BaseActive);
            t.SetupGet(x => x.PointeuseDb).Returns("PTG");
            t.Setup(x => x.GetPointeuseRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddPointeuse { Id = 1, NomBd = "PTG" });
            t.Setup(x => x.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { Id = 1, NomBd = BaseActive, TypeBase = "STANDARD" });
            return t.Object;
        }

        public PayrollClosureGuard Guard() => new(Uow, Tenant());

        public PunchService Punches() => new(Uow, Tenant(), new HistoryArchiveService(Uow), new ValidatedWeekGuard(Uow), Guard());
        public PunchImportService Imports() => new(Uow, Tenant(), new FakeExternalSourceService(), Guard());
        public AnomalyService Anomalies() => new(Uow, Tenant(), new HistoryArchiveService(Uow), new ValidatedWeekGuard(Uow), Guard());
        public CorrectedHoursService Corrections() => new(Uow, Tenant(), new ValidatedWeekGuard(Uow), Guard());
        public PunchProcessingService Preprocess() => new(Uow, Tenant(), Guard());
        public EmployeePlanningService Planning() => new(Uow, Tenant(), Mock.Of<Application.Abstractions.EmployeePlanning.IPlanningStatisticsReader>(),
            new PlanningDatabaseFilter(Tenant()), Guard());
        public WeeklyValidationService Weekly() => new(Uow, Tenant(), Mock.Of<IOvertimeWeekContext>(), Guard());
        public OvertimeExoImpoService Calcul() => new(Uow, Tenant(), new FakeExternalSourceService(), Mock.Of<IOvertimeWeekContext>(), Guard());
        public OvertimeDetailService PurgeHs() => new(Uow, Tenant(), new HistoryArchiveService(Uow), Mock.Of<IOvertimeWeekContext>(), Guard());
        public CanteenService Cantine() => new(Uow, Tenant(), Mock.Of<IPunchAggregate>(), Mock.Of<IPayrollCumulSync>(), Guard());
        public TravelExpenseService Frais() => new(Uow, Tenant(), Mock.Of<IPunchAggregate>(), Mock.Of<IPayrollCumulSync>(), Guard());
        public PayrollCumulSync CumulSync() => new(Uow, Tenant(), new FakeExternalSourceService(), Mock.Of<ISagePayrollWriter>(), Guard());

        /// <summary>Empreinte simple de l'état des tables sources : nombre de lignes et dates.</summary>
        public string Etat() => string.Join("|",
            Uow.Get<Pointage>().Items.Select(p => $"P{p.IdPointage}:{p.DatePointage:yyyyMMdd}:{p.HeurePointage}"),
            Uow.Get<HeureCorrigee>().Items.Select(h => $"C{h.Id}:{h.DatePointageIn:yyyyMMdd}:{h.HEntree}"),
            Uow.Get<HeureAnomalie>().Items.Select(a => $"A{a.Id}:{a.DatePointageIn:yyyyMMdd}:{a.HEntree}"),
            Uow.Get<PlanningSalarie>().Items.Select(p => $"L{p.Id}:{p.DateP:yyyyMMdd}:{p.Ha}"),
            Uow.Get<HsValidee>().Items.Select(v => $"V{v.Id}:{v.ValidationHs}"),
            Uow.Get<HsExoImp>().Items.Count, Uow.Get<HsSemaine>().Items.Count, Uow.Get<Cantine>().Items.Count, Uow.Get<Frais>().Items.Count);
    }

    private static async Task Refuse(Ctx c, Func<Task> action)
    {
        var avant = c.Etat();
        (await FluentActions.Awaiting(action).Should().ThrowAsync<ConflictException>()).Which.Message.Should().Be(Message);
        c.Etat().Should().Be(avant, "un refus n'écrit rien");
    }

    private static PeriodRequest Periode(DateTime from, DateTime to) => new(null, null, from, to);

    // ---------- Pointages ----------

    [Fact]
    public async Task ClockImport_OverlappingAClosedPeriod_IsRefusedAsAWhole()
    {
        var c = new Ctx();

        await Refuse(c, () => c.Imports().ImportFromClockAsync(new ImportPunchesRequest(null, null, Fin, Fin.AddDays(7))));
        await c.Imports().ImportFromClockAsync(new ImportPunchesRequest(null, null, Voisin, Voisin.AddDays(6)));
    }

    [Fact]
    public async Task ExcelPunchImport_WithALateRowInAClosedPeriod_IsRefusedAsAWhole()
    {
        var c = new Ctx();
        var lignes = new[]
        {
            new Dictionary<string, string> { ["Matricule"] = "04634", ["Date"] = "24/08/2026", ["Heure"] = "08:00" },
            new Dictionary<string, string> { ["Matricule"] = "04634", ["Date"] = "20/08/2026", ["Heure"] = "08:00" },
        };

        await Refuse(c, () => c.Imports().ImportPunchesExcelAsync(lignes));
        (await c.Imports().ImportPunchesExcelAsync(lignes.Take(1).ToList())).Imported.Should().Be(1);
    }

    [Fact]
    public async Task PunchUpdate_InOrIntoAClosedPeriod_IsRefused()
    {
        var c = new Ctx();
        c.Uow.Get<Pointage>().Items.AddRange([
            new Pointage { IdPointage = 1, NomBddSage = Base, MatriculeSage = "04634", DatePointage = DansLaPeriode, HeurePointage = new TimeSpan(8, 0, 0) },
            new Pointage { IdPointage = 2, NomBddSage = Base, MatriculeSage = "04634", DatePointage = Voisin, HeurePointage = new TimeSpan(8, 0, 0) }]);

        await Refuse(c, () => c.Punches().UpdatePunchAsync(1, new PunchUpdateDto(null, new TimeSpan(9, 0, 0), null)));
        await Refuse(c, () => c.Punches().UpdatePunchAsync(2, new PunchUpdateDto(DansLaPeriode, null, null)));
        (await c.Punches().UpdatePunchAsync(2, new PunchUpdateDto(null, new TimeSpan(9, 0, 0), null))).Heure.Should().Be(new TimeSpan(9, 0, 0));
    }

    [Fact]
    public async Task PunchPurge_TouchingAClosedPeriod_IsRefused()
    {
        var c = new Ctx();
        c.Uow.Get<Pointage>().Items.Add(new Pointage { IdPointage = 1, NomBddSage = Base, MatriculeSage = "04634", DatePointage = DansLaPeriode, HeurePointage = new TimeSpan(8, 0, 0) });

        await Refuse(c, () => c.Punches().PurgePunchesAsync(Periode(DansLaPeriode, DansLaPeriode)));
        // La purge lit jusqu'au lendemain de la fin : la veille d'une période clôturée est donc refusée aussi.
        await Refuse(c, () => c.Punches().PurgePunchesAsync(Periode(new DateTime(2026, 8, 10), new DateTime(2026, 8, 16))));
        await c.Punches().PurgePunchesAsync(Periode(Voisin, Voisin.AddDays(6)));
    }

    [Fact]
    public async Task Preprocess_OverlappingAClosedPeriod_IsRefused()
    {
        var c = new Ctx();

        await Refuse(c, () => c.Preprocess().ProcessAsync(Periode(new DateTime(2026, 8, 10), DansLaPeriode)));
    }

    // ---------- Anomalies (arbitrage) ----------

    [Fact]
    public async Task Anomalies_UpdateValidateDelete_InAClosedPeriod_AreRefused()
    {
        var c = new Ctx();
        c.Uow.Get<HeureAnomalie>().Items.AddRange([
            new HeureAnomalie { Id = 1, BddSage = Base, MatriculeSage = "04634", DatePointageIn = DansLaPeriode, DatePointageOut = DansLaPeriode,
                HEntree = new TimeSpan(8, 0, 0), HSortie = new TimeSpan(17, 0, 0) },
            new HeureAnomalie { Id = 2, BddSage = Base, MatriculeSage = "04634", DatePointageIn = Voisin, DatePointageOut = Voisin,
                HEntree = new TimeSpan(8, 0, 0), HSortie = new TimeSpan(17, 0, 0) }]);

        await Refuse(c, () => c.Anomalies().UpdateAnomalyAsync(1, new AnomalyUpdateDto { HEntree = new TimeSpan(9, 0, 0) }));
        await Refuse(c, () => c.Anomalies().UpdateAnomalyAsync(2, new AnomalyUpdateDto { DateIn = DansLaPeriode, DateOut = DansLaPeriode }));
        await Refuse(c, () => c.Anomalies().ValidateAnomaliesAsync([1m, 2m]));
        await Refuse(c, () => c.Anomalies().DeleteAnomalyAsync(1));
        await c.Anomalies().UpdateAnomalyAsync(2, new AnomalyUpdateDto { HEntree = new TimeSpan(9, 0, 0) });
    }

    // ---------- Heures corrigées ----------

    [Fact]
    public async Task CorrectedHours_CreateUpdateDeleteImport_InAClosedPeriod_AreRefused()
    {
        var c = new Ctx();
        c.Uow.Get<HeureCorrigee>().Items.Add(new HeureCorrigee { Id = 1, BddSage = Base, MatriculeSage = "04634", DatePointageIn = DansLaPeriode, HEntree = new TimeSpan(8, 0, 0) });

        await Refuse(c, () => c.Corrections().SaveCorrectionAsync(new CorrectedHourDto { Matricule = "04634", DateIn = DansLaPeriode, HEntree = new TimeSpan(8, 0, 0) }));
        await Refuse(c, () => c.Corrections().SaveCorrectionAsync(new CorrectedHourDto { Id = 1, Matricule = "04634", DateIn = Voisin, HEntree = new TimeSpan(8, 0, 0) }));
        await Refuse(c, () => c.Corrections().DeleteCorrectionAsync(1));
        await Refuse(c, () => c.Corrections().ImportCorrectionsExcelAsync([
            new Dictionary<string, string> { ["Matricule"] = "04634", ["DateIn"] = "20/08/2026", ["HEntree"] = "08:00" }]));
        (await c.Corrections().SaveCorrectionAsync(new CorrectedHourDto { Matricule = "04634", DateIn = Voisin, HEntree = new TimeSpan(8, 0, 0) }))
            .DateIn.Should().Be(Voisin);
    }

    // ---------- Planning ----------

    [Fact]
    public async Task Planning_SaveMoveDelete_InAClosedPeriod_AreRefused()
    {
        var c = new Ctx();
        c.Uow.Get<CardPaie>().Items.Add(new CardPaie { Id = 7, SageMatricule = "04634", SageBdd = Base, PointeuseBdd = "PTG" });
        c.Uow.Get<PlanningSalarie>().Items.AddRange([
            new PlanningSalarie { Id = 1, IdSal = 7, DateP = DansLaPeriode, Ha = DansLaPeriode.AddHours(8), Hd = DansLaPeriode.AddHours(17), BddSage = Base, BddPointeuse = "PTG" },
            new PlanningSalarie { Id = 2, IdSal = 7, DateP = Voisin, Ha = Voisin.AddHours(8), Hd = Voisin.AddHours(17), BddSage = Base, BddPointeuse = "PTG" }]);
        EmployeePlanningDto Jour(int id, DateTime d) => new(id, 7, null, d, d.AddHours(9), TimeSpan.Zero, d.AddHours(18), false);

        await Refuse(c, () => c.Planning().SaveEmployeePlanningAsync([Jour(0, DansLaPeriode)]));
        await Refuse(c, () => c.Planning().SaveEmployeePlanningAsync([Jour(1, DansLaPeriode)]));
        await Refuse(c, () => c.Planning().SaveEmployeePlanningAsync([Jour(1, Voisin.AddDays(1))]), "déplacer une journée hors d'une période clôturée la modifie");
        await Refuse(c, () => c.Planning().SaveEmployeePlanningAsync([Jour(0, Voisin), Jour(0, DansLaPeriode)]));
        await Refuse(c, () => c.Planning().DeleteEmployeePlanningAsync(1));
        await c.Planning().SaveEmployeePlanningAsync([Jour(0, Voisin)]);
        c.Uow.Get<PlanningSalarie>().Items.Single(p => p.Id == 2).Ha.Should().Be(Voisin.AddHours(9));
    }

    private static async Task Refuse(Ctx c, Func<Task> action, string because)
    {
        var avant = c.Etat();
        (await FluentActions.Awaiting(action).Should().ThrowAsync<ConflictException>(because)).Which.Message.Should().Be(Message);
        c.Etat().Should().Be(avant, because);
    }

    // ---------- Validation de semaine, calcul et purge HS ----------

    [Fact]
    public async Task WeeklyValidation_ValidateUnvalidatePurge_OfAClosedWeek_AreRefused()
    {
        var c = new Ctx();
        c.Uow.Get<HsValidee>().Items.Add(new HsValidee { Id = 1, BddSage = Base, Matricules = "04634", DateDeb = Debut, DateFin = Fin, ValidationHs = true });
        var semaine = new WeeklyValidationRequest(null, null, Debut, Fin, ["04634"]);

        await Refuse(c, () => c.Weekly().ValidateWeekAsync(semaine, validate: true));
        await Refuse(c, () => c.Weekly().ValidateWeekAsync(semaine, validate: false));
        await Refuse(c, () => c.Weekly().PurgeValidationAsync(Periode(Debut, Fin)));
    }

    [Fact]
    public async Task OvertimeCalculateAndPurge_OfAClosedPeriod_AreRefused()
    {
        var c = new Ctx();
        c.Uow.Get<HsExoImp>().Items.Add(new HsExoImp { Oid = 1, BddSage = Base, Matricule = "04634", PeriodeDebut = Debut, PeriodeFin = Fin, Exo130 = 1m });

        await Refuse(c, () => c.Calcul().CalculateExoImpoAsync(Periode(Debut, Fin)));
        await Refuse(c, () => c.PurgeHs().PurgeHsAsync(Periode(Debut, Fin)));
        await Refuse(c, () => c.Calcul().CalculateExoImpoAsync(Periode(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31))));
    }

    // ---------- Cantine et frais ----------

    [Fact]
    public async Task CanteenAndTravel_ComputePurgeSync_OfAClosedPeriod_AreRefused()
    {
        var c = new Ctx();

        await Refuse(c, () => c.Cantine().ComputeCanteenAsync(Periode(Debut, Fin)));
        await Refuse(c, () => c.Cantine().PurgeCanteenAsync(Periode(Debut, Fin)));
        await Refuse(c, () => c.Frais().ComputeTravelAsync(Periode(Debut, Fin)));
        await Refuse(c, () => c.CumulSync().SyncExtrasAsync(Periode(Debut, Fin), "HA05", cantine: true, CancellationToken.None));
        await Refuse(c, () => c.CumulSync().SyncExtrasAsync(Periode(Debut, Fin), "HA06", cantine: false, CancellationToken.None));
    }

    // ---------- Ce qui reste ouvert ----------

    [Fact]
    public async Task WithoutClosure_OrOnAnotherBase_EverythingStaysModifiable()
    {
        foreach (var c in new[] { new Ctx(cloturee: false), new Ctx { BaseActive = "AUTRE_BASE" } })
        {
            c.Uow.Get<HeureCorrigee>().Items.Add(new HeureCorrigee { Id = 1, BddSage = c.BaseActive, MatriculeSage = "04634", DatePointageIn = DansLaPeriode, HEntree = new TimeSpan(8, 0, 0) });
            await c.Corrections().SaveCorrectionAsync(new CorrectedHourDto { Matricule = "04634", DateIn = DansLaPeriode, HEntree = new TimeSpan(8, 0, 0) });
            await c.Corrections().DeleteCorrectionAsync(1);
            await c.Imports().ImportPunchesExcelAsync([new Dictionary<string, string> { ["Matricule"] = "04634", ["Date"] = "20/08/2026", ["Heure"] = "08:00" }]);
            c.Uow.Get<Pointage>().Items.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task CommonCheck_IsFast_OnALargeBatch_AndReadsTheClosuresOnce()
    {
        var c = new Ctx();
        for (var i = 2; i < 200; i++)
            c.Uow.Get<PaieCloture>().Items.Add(new PaieCloture { Id = i, BddSage = Base, PeriodeDebut = new DateTime(2000, 1, 1).AddDays(7 * i), PeriodeFin = new DateTime(2000, 1, 7).AddDays(7 * i) });
        var guard = c.Guard();
        var dates = Enumerable.Range(0, 50_000).Select(i => new DateTime(2026, 9, 1).AddMinutes(i)).ToList();

        var chrono = Stopwatch.StartNew();
        await guard.EnsureOpenAsync(dates);
        for (var i = 0; i < 1000; i++)
            await guard.EnsureOpenAsync(Voisin, Voisin);
        chrono.Stop();

        chrono.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        (await guard.ClosedPeriodsAsync()).Should().HaveCount(199);
    }
}
