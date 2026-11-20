using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>Contrôle avant paie : aucune anomalie ni ligne de checklist pour une variable non envoyée à la paie.</summary>
public class ClosingPayrollVariablesTests
{
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime Mercredi = Lundi.AddDays(2);
    private static readonly DateTime Vendredi = Lundi.AddDays(4);
    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);

    [Fact]
    public async Task Analyse_DefaultConfiguration_IsIdenticalToTheAnalysisWithoutVariableService()
    {
        // Arrange
        var historique = Contexte(variables: null);
        var configure = Contexte(new FixedVariableService(PayrollVariableSelection.Default));

        // Act
        var avant = await historique.AnalyserAsync(Periode());
        var apres = await configure.AnalyserAsync(Periode());

        // Assert
        apres.Should().BeEquivalentTo(avant);
        avant.Anomalies.Items.Should().Contain(a => a.Type == nameof(ClosingAnomalyType.FerieNonApplique));
    }

    [Fact]
    public async Task Analyse_FerieUnchecked_ReportsNoHolidayAnomaly_ButKeepsTheOthers()
    {
        // Arrange
        var sansFerie = PayrollVariableSelection.Avec(PayrollVariableSelection.HeuresSup.Where(v => v != PayrollVariableSelection.Ferie));
        var service = Contexte(new FixedVariableService(sansFerie));
        var reference = await Contexte(variables: null).AnalyserAsync(Periode());

        // Act
        var resultat = await service.AnalyserAsync(Periode());

        // Assert
        resultat.Anomalies.Items.Should().NotContain(a => a.Type == nameof(ClosingAnomalyType.FerieNonApplique));
        resultat.Anomalies.Items.Select(a => a.Type).Should().BeEquivalentTo(
            reference.Anomalies.Items.Select(a => a.Type).Where(t => t != nameof(ClosingAnomalyType.FerieNonApplique)));
    }

    [Fact]
    public void AnalysisCacheKey_DependsOnIgnoredTypes_AndIsUnchangedByDefault()
    {
        var requete = Periode() with { Options = ClosingRulesOptions.Default };
        var ignore = requete with { Options = ClosingRulesOptions.Default with { TypesIgnores = new HashSet<ClosingAnomalyType> { ClosingAnomalyType.FerieNonApplique } } };

        ClosingAnalysisCache.Cle("S", "P", requete).Should().NotContain("sans:");
        ClosingAnalysisCache.Cle("S", "P", ignore).Should().NotBe(ClosingAnalysisCache.Cle("S", "P", requete));
        (ClosingRulesOptions.Default with { }).Should().Be(ClosingRulesOptions.Default);
    }

    [Fact]
    public void Checklist_TaxableHoursLine_IsAbsentWhenBothImpoVariablesAreUnchecked()
    {
        // Arrange
        var donnees = new ClosingChecklistData(Pointages: 10, SalariesPointes: 2, Anomalies: 0, Corrections: 5,
            SemainesAttendues: 2, SemainesValidees: 2, ExoExactes: 3, ExoRecoupantes: 3, PeriodesExoDistinctes: 1,
            SalariesImposables: null, DernierResumeSynchro: null);

        // Act
        var sans = ClosingChecklistLines.Construire(donnees);
        var avec = ClosingChecklistLines.Construire(donnees with { SalariesImposables = 1 });

        // Assert
        sans.Select(l => l.Code).Should().NotContain("HeuresImposables");
        avec.Select(l => l.Code).Should().Contain("HeuresImposables");
        avec.Should().HaveCount(sans.Count + 1);
    }

    private static ClosingAnalysisRequest Periode() => new(Lundi, Vendredi, IncludeInformation: true);

    private static ClosingService Contexte(Application.Abstractions.Payroll.IPayrollVariableService? variables)
    {
        var uow = new InMemoryUnitOfWork();
        uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
        uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = true });
        uow.Get<CardPaie>().Items.Add(new CardPaie { Id = 1, SageMatricule = "001", Departement = "D", SageBdd = "SAGE_TEST", PointeuseBdd = "PTE_TEST" });
        for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
        {
            uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
            {
                Id = uow.Get<PlanningSalarie>().Items.Count + 1, IdSal = 1, DateP = jour, Ha = jour.Add(H8), Hd = jour.Add(H17),
                OffP = false, BddSage = "SAGE_TEST", BddPointeuse = "PTE_TEST"
            });
            if (jour != Lundi.AddDays(1))
                foreach (var heure in new[] { H8, H17 })
                    uow.Get<Pointage>().Items.Add(new Pointage
                    {
                        MatriculeSage = "001", DatePointage = jour, HeurePointage = heure, NomBddSage = "SAGE_TEST", NomBddPointeuse = "PTE_TEST"
                    });
        }
        uow.Get<JourFerie>().Items.Add(new JourFerie { Date = Mercredi, BddSage = "SAGE_TEST" });
        var tenant = new TenantConnectionService(uow, new FakeCompanyContext(), new FakeCurrentUser(),
            SoftTime.Tests.Http.TestRights.Service(uow), NullLogger<TenantConnectionService>.Instance);
        return new ClosingService(uow, tenant, new SageLeaveCalendar(tenant, new FakeExternalSourceService()),
            new HorlogeFixe(), Options.Create(new ClosingOptions()), variables: variables);
    }

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(new DateTime(2025, 12, 1, 10, 0, 0), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
