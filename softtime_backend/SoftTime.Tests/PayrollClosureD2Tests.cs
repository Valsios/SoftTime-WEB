using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.SchemaMigrations;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Lot D2 : action de clôture (contrôles, refus explicites, journal, caches), refus d'un nouvel envoi d'une période
/// clôturée, droit SAGE:{base}:CLOTURE hors défauts, absence de tout chemin de déblocage, migration 010.
/// </summary>
public class PayrollClosureD2Tests
{
    private const string Base = "SFOI_PAIE2025";
    private static readonly DateTime Debut = new(2026, 8, 17);
    private static readonly DateTime Fin = new(2026, 8, 23);
    private static readonly DateTimeOffset Aujourdhui = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Environnement de clôture : base 3033, un envoi réussi sans filtre avec instantané concordant, Contrôle « Prêt ».</summary>
    private sealed class Ctx
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public Mock<IClosingService> Controle { get; } = new();
        public Mock<IClosingChecklistCache> Checklist { get; } = new();
        public ClosingOptions Options { get; } = new() { Enabled = true };
        public HashSet<string> Permissions { get; } = new(StringComparer.OrdinalIgnoreCase) { $"SAGE:{Base}:CLOTURE" };
        public decimal Role { get; set; } = 91;
        public string Statut { get; set; } = "Pret";
        public PayrollSendLog SendLog { get; }
        public PayrollClosureGuard Guard { get; }

        public Ctx()
        {
            SendLog = new PayrollSendLog(new ScopeFactory(Uow), NullLogger<PayrollSendLog>.Instance);
            Guard = new PayrollClosureGuard(Uow, Tenant());
            Controle.Setup(c => c.AnalyserAsync(It.IsAny<ClosingAnalysisRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Analyse(Statut));
        }

        public PayrollClosureService Sut()
        {
            var rights = new Mock<IEffectiveRightsService>();
            rights.Setup(r => r.GetAsync(It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new EffectiveRights
                {
                    RoleId = Role, Gates = new HashSet<int> { 1 }, Permissions = new HashSet<string>(Permissions, StringComparer.OrdinalIgnoreCase)
                });
            var horloge = new Mock<TimeProvider>();
            horloge.Setup(h => h.GetUtcNow()).Returns(Aujourdhui);
            horloge.Setup(h => h.LocalTimeZone).Returns(TimeZoneInfo.Utc);
            return new PayrollClosureService(Uow, Tenant(), new Http.TestCurrentUser(42, Role), rights.Object, Controle.Object,
                Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(new AuditJournalOptions { Enabled = true }),
                horloge.Object, new PayrollVariableCache(), new ClosingAnalysisCache(), Checklist.Object, Guard, Mock.Of<ICompanyContext>(),
                NullLogger<PayrollClosureService>.Instance);
        }

        /// <summary>Heures actuelles (T_HSExoImp) d'un salarié sur la période.</summary>
        public void Heures(string matricule, decimal exo130, DateTime? debut = null, DateTime? fin = null) =>
            Uow.Get<HsExoImp>().Items.Add(new HsExoImp
            {
                Oid = Uow.Get<HsExoImp>().Items.Count + 1, Matricule = matricule, NumSal = 103, Exo130 = exo130,
                PeriodeDebut = debut ?? Debut, PeriodeFin = fin ?? Fin, BddSage = Base
            });

        /// <summary>Envoi avec instantané (une valeur Exo130 pour 04634).</summary>
        public Task<int> EnvoiAsync(string statut = "SUCCES", PayrollSendFilter? filtre = null, decimal valeur = 1.5m, DateTime? debut = null, DateTime? fin = null)
            => SendLog.RecordWithSnapshotAsync(new PayrollSend(Base, "STANDARD", debut ?? Debut, fin ?? Fin, "rh", statut, 1),
                new PayrollSnapshotDraft([new PayrollSnapshotLine("04634", 103, "Exo130", "HS01", "H", valeur, false)], 0, 0,
                    filtre ?? PayrollSendFilter.Aucun, true, 8m));

        public static ClosingAnalysisDto Analyse(string? statut) => new(
            Debut, Fin, null, statut == "Pret" ? 100 : 60, "f", statut is null ? 0 : 5, statut == "Pret" ? 0 : 3,
            [new ClosingCountDto("Critique", statut == "Pret" ? 0 : 2), new ClosingCountDto("Mineure", statut == "Pret" ? 0 : 1)],
            statut == "Pret" ? [] : [new ClosingCountDto("PointageManquant", 2), new ClosingCountDto("RetardImportant", 1)],
            new PagedResult<ClosingAnomalyDto>(statut == "Pret" ? [] :
                [new ClosingAnomalyDto("PointageManquant", "Critique", 10, "04634", "BR704", new DateTime(2026, 8, 18), "d", null)], statut == "Pret" ? 0 : 1, 1, 10, 1),
            [], null, statut);
    }

    private static ITenantConnectionService Tenant(string baseSage = Base)
    {
        var tenant = new Mock<ITenantConnectionService>();
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { Id = 3033, NomBd = baseSage, TypeBase = "STANDARD" });
        tenant.SetupGet(t => t.SageDb).Returns(baseSage);
        return tenant.Object;
    }

    private static async Task<Ctx> PretAsync()
    {
        var c = new Ctx();
        c.Heures("04634", 1.5m);
        await c.EnvoiAsync();
        return c;
    }

    // ---------- Clôture réussie ----------

    [Fact]
    public async Task Close_AllConditionsMet_FreezesTheLastSend_AndJournalsTheAuthor()
    {
        var c = await PretAsync();

        var cloture = await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        var ligne = c.Uow.Get<PaieCloture>().Items.Single();
        ligne.BddSage.Should().Be(Base);
        ligne.PeriodeDebut.Should().Be(Debut);
        ligne.PeriodeFin.Should().Be(Fin);
        ligne.CloturePar.Should().Be("u42");
        ligne.StatutControle.Should().Be("Pret");
        ligne.ResumeControle.Should().StartWith("Statut=Prêt; Score=100; JoursContrôlés=5; Anomalies=0; Critiques=0");
        ligne.Empreinte.Should().Be(c.Uow.Get<PaieInstantane>().Items.Single().Empreinte);
        ligne.EnvoiId.Should().Be(c.Uow.Get<PaieEnvoi>().Items.Single().Id);
        cloture.Empreinte.Should().MatchRegex("^[0-9a-f]{64}$");
        var journal = c.Uow.Get<JournalAudit>().Items.Single(j => j.Actions == "CLOTURE_PAIE");
        journal.Users.Should().Be("u42");
        journal.BaseSage.Should().Be(Base);
        journal.Nouveaux.Should().Contain($"Empreinte={ligne.Empreinte}").And.Contain("Contrôle=Statut=Prêt");
        c.Checklist.Verify(x => x.Vider(), Times.Once);
        // Le Contrôle est relancé sans cache, sur la base entière (sans département).
        c.Controle.Verify(x => x.AnalyserAsync(It.Is<ClosingAnalysisRequest>(r =>
            !r.ReuseCached && r.Departement == null && r.From == Debut && r.To == Fin), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Close_Administrator_IsAllowedWithoutTheExplicitPermission()
    {
        var c = await PretAsync();
        c.Permissions.Clear();
        c.Role = 1;

        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        c.Uow.Get<PaieCloture>().Items.Should().ContainSingle();
    }

    // ---------- Refus ----------

    [Fact]
    public async Task Close_WithoutAnySend_IsRefused()
    {
        var c = new Ctx();

        await Refus(c, PayrollClosureService.AucunEnvoi);
    }

    [Fact]
    public async Task Close_UncertainLastSend_IsRefused()
    {
        var c = await PretAsync();
        await c.SendLog.CompleteAsync(c.Uow.Get<PaieEnvoi>().Items.Single().Id, "INCERTAIN", 1);

        await Refus(c, PayrollClosureService.EnvoiIncertain);
    }

    [Theory]
    [InlineData("ECHEC")]
    [InlineData("EN_COURS")]
    public async Task Close_LastSendNotSuccessful_IsRefused(string statut)
    {
        var c = await PretAsync();
        c.Uow.Get<PaieEnvoi>().Items.Add(new PaieEnvoi
        {
            Id = 77, BddSage = Base, PeriodeDebut = Debut, PeriodeFin = Fin, Statut = statut, TypeBase = "STANDARD", EnvoyeLe = DateTime.UtcNow.AddMinutes(5)
        });

        await Refus(c, $"Le dernier envoi de la période n'a pas réussi (statut {statut}). Relancez l'envoi sans filtre avant de clôturer.");
    }

    [Fact]
    public async Task Close_FilteredLastSend_IsRefused_WithTheExpectedMessage()
    {
        var c = new Ctx();
        c.Heures("04634", 1.5m);
        await c.EnvoiAsync();
        await c.EnvoiAsync(filtre: PayrollSendFilter.De("BR704", null, null));

        var ex = await Refus(c, PayrollClosureService.EnvoiFiltre);
        ex.Message.Should().Contain("Lancez un envoi sans filtre avant de clôturer");
    }

    [Fact]
    public async Task Close_SendWithoutSnapshot_IsRefused()
    {
        var c = new Ctx();
        await c.SendLog.RecordAsync(new PayrollSend(Base, "STANDARD", Debut, Fin, "rh", "SUCCES", 1));

        await Refus(c, PayrollClosureService.SansInstantane);
    }

    [Theory]
    [InlineData("ACorriger", "« À corriger »")]
    [InlineData("Bloquant", "« Bloquant »")]
    public async Task Close_ControlNotReady_IsRefused_WithThePointsToFix(string statut, string libelle)
    {
        var c = await PretAsync();
        c.Statut = statut;

        var ex = await Refus(c, null);

        ex.Message.Should().StartWith($"Contrôle avant paie {libelle} sur la base entière : clôture refusée, le statut « Prêt » est exigé.")
            .And.Contain("Points à corriger : 2 anomalie(s) critique(s) sur 3 ; types : PointageManquant (2), RetardImportant (1).")
            .And.Contain("Anomalies critiques : 18/08/2026 04634 PointageManquant.");
    }

    [Fact]
    public async Task Close_ControlWithoutStatus_IsRefused()
    {
        var c = await PretAsync();
        c.Statut = null!;

        var ex = await Refus(c, null);
        ex.Message.Should().StartWith("Contrôle avant paie sans statut");
    }

    [Fact]
    public async Task Close_ControlDisabled_IsRefused()
    {
        var c = await PretAsync();
        c.Options.Enabled = false;

        await Refus(c, PayrollClosureService.ControleDesactive);
    }

    [Fact]
    public async Task Close_Twice_IsRefused_AndOverlappingPeriodToo()
    {
        var c = await PretAsync();
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        await Refus(c, "Période déjà clôturée (du 17/08/2026 au 23/08/2026) : une clôture est définitive.");
        var recoupe = await FluentActions.Awaiting(() => c.Sut().CloseAsync(new PayrollClosureRequest(Debut.AddDays(3), Fin.AddDays(3))))
            .Should().ThrowAsync<ConflictException>();
        recoupe.Which.Message.Should().Be("La période recoupe une période déjà clôturée (du 17/08/2026 au 23/08/2026) : clôture refusée.");
        c.Uow.Get<PaieCloture>().Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Close_AlteredSnapshot_IsRefused()
    {
        var c = await PretAsync();
        c.Uow.Get<PaieInstantaneLigne>().Items.Single().Valeur = 9m;

        await Refus(c, PayrollClosureService.InstantaneAltere);
    }

    [Fact]
    public async Task Close_HoursRecalculatedSinceTheSend_IsRefused()
    {
        var c = await PretAsync();
        c.Uow.Get<HsExoImp>().Items.Single().Exo130 = 2m;

        var ex = await Refus(c, null);
        ex.Message.Should().StartWith("Les heures actuelles de la période ne correspondent plus à l'envoi figé (1 valeur(s) différente(s), par exemple Exo130 1.5 figé, 2 actuel)");
    }

    [Fact]
    public async Task Close_PeriodNotFinished_IsRefused()
    {
        var c = new Ctx();
        var act = () => c.Sut().CloseAsync(new PayrollClosureRequest(new DateTime(2026, 11, 9), new DateTime(2026, 11, 18)));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(PayrollClosureService.PeriodeNonTerminee);
    }

    [Theory]
    [InlineData("SAGE:SFOI_PAIE2025:UPDATE")]
    [InlineData("SAGE:AUTRE_BASE:CLOTURE")]
    [InlineData("REPORTS:CLOSING")]
    public async Task Close_WithoutTheClosingPermissionOfTheBase_IsForbidden(string seulePermission)
    {
        var c = await PretAsync();
        c.Permissions.Clear();
        c.Permissions.Add(seulePermission);

        await FluentActions.Awaiting(() => c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin))).Should().ThrowAsync<ForbiddenException>();
        c.Uow.Get<PaieCloture>().Items.Should().BeEmpty();
    }

    // ---------- Après la clôture ----------

    [Fact]
    public async Task AfterClosure_ANewSendOfThePeriodIsRefused_BeforeAnyWrite()
    {
        var c = await PretAsync();
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));
        var writer = new Mock<IPayrollWriter>();
        writer.Setup(w => w.CanHandle(It.IsAny<BddSage>())).Returns(true);
        var sync = new Infrastructure.Services.Overtime.OvertimePayrollSyncService(c.Uow, Tenant(), new PayrollWriterResolver([writer.Object]), null,
            Mock.Of<Application.Abstractions.Overtime.IOvertimeWeekContext>(), closures: new PayrollClosureGuard(c.Uow, Tenant()));

        var ex = await FluentActions.Awaiting(() => sync.SyncPayrollAsync(new PeriodRequest(null, null, Debut.AddDays(2), Fin.AddDays(2), null)))
            .Should().ThrowAsync<ConflictException>();

        ex.Which.Message.Should().Be("Période clôturée (du 17/08/2026 au 23/08/2026) : nouvel envoi en paie impossible. Les corrections se font sur la période suivante.");
        writer.Verify(w => w.WriteAsync(It.IsAny<BddSage>(), It.IsAny<IReadOnlyList<HsExoDto>>(), It.IsAny<PayrollSyncContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AfterClosure_ASendStartedBefore_CannotFreezeItsSnapshot()
    {
        var c = await PretAsync();
        var enCours = await c.SendLog.RecordAsync(new PayrollSend(Base, "STANDARD", Debut, Fin, "rh", "EN_COURS", 1));
        c.Uow.Get<PaieEnvoi>().Items.Single(e => e.Id == enCours).EnvoyeLe = DateTime.UtcNow.AddDays(-1);
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        var draft = new PayrollSnapshotDraft([], 0, 0, PayrollSendFilter.Aucun, true, 8m);
        await FluentActions.Awaiting(() => c.SendLog.CompleteWithSnapshotAsync(enCours, "SUCCES", 1, draft)).Should().ThrowAsync<ConflictException>();
        await FluentActions.Awaiting(() => c.EnvoiAsync()).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Guard_RefusesOnlyTheClosedPeriodOfTheActiveBase()
    {
        var c = await PretAsync();
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));
        var guard = new PayrollClosureGuard(c.Uow, Tenant());

        (await FluentActions.Awaiting(() => guard.EnsureOpenAsync([new DateTime(2026, 8, 20)])).Should().ThrowAsync<ConflictException>())
            .Which.Message.Should().Be("Période clôturée (du 17/08/2026 au 23/08/2026) : modification impossible. Les corrections se font sur la période suivante.");
        await FluentActions.Awaiting(() => guard.EnsureOpenAsync(Fin, Fin)).Should().ThrowAsync<ConflictException>();
        await guard.EnsureOpenAsync([Debut.AddDays(-1), Fin.AddDays(1)]);
        await guard.EnsureOpenAsync(Fin.AddDays(1), Fin.AddDays(7));
        await new PayrollClosureGuard(c.Uow, Tenant("AUTRE_BASE")).EnsureOpenAsync(Debut, Fin);
    }

    [Fact]
    public async Task Guard_WithoutAnyClosure_NeverRefuses()
    {
        var guard = new PayrollClosureGuard(new InMemoryUnitOfWork(), Tenant());

        await guard.EnsureOpenAsync(DateTime.MinValue.AddDays(1), DateTime.MaxValue.AddDays(-1));
        await guard.EnsureOpenAsync(Enumerable.Range(0, 1000).Select(i => Debut.AddDays(i)));
    }

    [Fact]
    public async Task ListPeriods_ShowsSentAndClosedStates_AndTheRightToClose()
    {
        var c = await PretAsync();
        await c.EnvoiAsync(debut: new DateTime(2026, 9, 14), fin: new DateTime(2026, 10, 11));
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        var periodes = await c.Sut().ListPeriodsAsync();

        periodes.PeutCloturer.Should().BeTrue();
        periodes.Periodes.Select(p => (p.PeriodeDebut, p.Etat)).Should().Equal((new DateTime(2026, 9, 14), "ENVOYEE"), (Debut, "CLOTUREE"));
        periodes.Periodes[1].Cloture!.CloturePar.Should().Be("u42");
        c.Permissions.Clear();
        c.Permissions.Add($"SAGE:{Base}:UPDATE");
        (await c.Sut().ListPeriodsAsync()).PeutCloturer.Should().BeFalse();
        c.Permissions.Clear();
        await FluentActions.Awaiting(() => c.Sut().ListPeriodsAsync()).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task State_ReportsTheClosedPeriodsOfASelection()
    {
        var c = await PretAsync();
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        var etat = await c.Sut().StateAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        var voisine = await c.Sut().StateAsync(Fin.AddDays(1), Fin.AddDays(7));

        etat.Cloturee.Should().BeTrue();
        etat.Message.Should().StartWith("Période clôturée (du 17/08/2026 au 23/08/2026)");
        voisine.Cloturee.Should().BeFalse();
    }

    // ---------- Aucun déblocage ----------

    [Fact]
    public void NoUnlockPath_InTheServiceNorTheController()
    {
        typeof(IPayrollClosureService).GetMethods().Select(m => m.Name)
            .Should().BeEquivalentTo("CloseAsync", "ListPeriodsAsync", "StateAsync");
        var verbes = typeof(Api.Controllers.PayrollClosuresController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), true))
            .Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
            .SelectMany(a => a.HttpMethods)
            .Distinct()
            .ToList();
        verbes.Should().BeEquivalentTo("POST", "GET");
        typeof(PaieCloture).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .Where(m => m.DeclaringType?.Namespace?.StartsWith("SoftTime", StringComparison.Ordinal) == true)
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Deblo", StringComparison.OrdinalIgnoreCase) || n.Contains("Unlock", StringComparison.OrdinalIgnoreCase)
                                      || n.Contains("Reopen", StringComparison.OrdinalIgnoreCase) || n.Contains("Rouvr", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Droit dédié ----------

    [Fact]
    public void ClosingPermission_IsValidForSageOnly_AndNeverGrantedByDefault()
    {
        PermissionCodeValidator.IsValid("SAGE:SFOI_PAIE2025:CLOTURE").Should().BeTrue();
        PermissionCodeValidator.IsValid("sage:SFOI_PAIE2025:cloture").Should().BeTrue();
        PermissionCodeValidator.IsValid("POINTEUSE:PTG:CLOTURE").Should().BeFalse();
        PermissionCodeValidator.Normalize(["sage:Ma_Base:cloture"], "-").Should().Equal("SAGE:Ma_Base:CLOTURE");
        PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { 1, 2, 3, 4, 5, 6 }, [new BddSage { NomBd = Base }], [])
            .Should().NotContain(code => code.EndsWith(":CLOTURE", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Migration 010 ----------

    [Fact]
    public void Script010_Closures_CreatesATableAndATriggerOnlyByAdditions_AndIsReplayable()
    {
        var script = new EmbeddedMigrationScriptSource().Load().Should().ContainSingle(s => s.Version == 10).Which;
        script.Name.Should().Be("010_cloture_paie.sql");
        var code = script.Content[(script.Content.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        foreach (var interdit in new[] { "DROP ", "TRUNCATE", "ALTER COLUMN", "ALTER TABLE", "USE [", "CREATE DATABASE", "INSERT ", "DISABLE TRIGGER" })
            code.Should().NotContainEquivalentOf(interdit);
        code.Should().Contain("OBJECT_ID(N'dbo.T_PAIE_CLOTURE', N'U') IS NULL")
            .And.Contain("name = N'UX_T_PAIE_CLOTURE_BDD_PERIODE'")
            .And.Contain("OBJECT_ID(N'dbo.TR_T_PAIE_CLOTURE_IMMUABLE', N'TR') IS NULL")
            .And.Contain("INSTEAD OF UPDATE, DELETE")
            .And.Contain("THROW 51992")
            .And.Contain("REFERENCES dbo.T_PAIE_INSTANTANE (ID)");
    }

    // ---------- Aides ----------

    private static async Task<ConflictException> Refus(Ctx c, string? message)
    {
        var avant = c.Uow.Get<PaieCloture>().Items.Count;
        var ex = (await FluentActions.Awaiting(() => c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin)))
            .Should().ThrowAsync<ConflictException>()).Which;
        if (message is not null)
            ex.Message.Should().Be(message);
        c.Uow.Get<PaieCloture>().Items.Should().HaveCount(avant, "un refus n'écrit rien");
        return ex;
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
