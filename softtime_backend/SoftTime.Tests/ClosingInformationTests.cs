using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>
/// Phase 3c : niveau « Information », doublons selon le mode de pointage, tolérance de 30 s,
/// salariés sans planning, <c>PointageExiste</c>, <c>includeInformation</c>, seuil de 5 %, réglages
/// invalides, et non-régression avec les anciens réglages.
/// </summary>
public class ClosingInformationTests
{
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime Mardi = Lundi.AddDays(1);
    private static readonly DateTime Mercredi = Lundi.AddDays(2);
    private static readonly DateTime Jeudi = Lundi.AddDays(3);
    private static readonly DateTime Vendredi = Lundi.AddDays(4);
    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);

    private static TimeSpan Plus(TimeSpan h, int secondes) => h.Add(TimeSpan.FromSeconds(secondes));

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(new DateTime(2025, 12, 1, 10, 0, 0), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public TenantConnectionService Tenant { get; }
        public ClosingService Service { get; }

        public Contexte(bool multiPointage, ClosingOptions? reglages = null)
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = multiPointage });
            Tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            Service = new ClosingService(Uow, Tenant, new SageLeaveCalendar(Tenant, new FakeExternalSourceService()),
                new HorlogeFixe(), Options.Create(reglages ?? new ClosingOptions()));
        }

        public void Carte(int id, string matricule) =>
            Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = id, SageMatricule = matricule, Departement = "D", SageBdd = "SAGE_TEST", PointeuseBdd = "PTE_TEST"
            });

        public void Plan(int idsal, DateTime jour, bool off = false) =>
            Uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
            {
                Id = Uow.Get<PlanningSalarie>().Items.Count + 1, IdSal = idsal, DateP = jour,
                Ha = jour.Add(H8), Hd = jour.Add(H17), OffP = off, BddSage = "SAGE_TEST", BddPointeuse = "PTE_TEST"
            });

        public void Punch(string matricule, DateTime jour, params TimeSpan[] heures)
        {
            foreach (var heure in heures)
                Uow.Get<Pointage>().Items.Add(new Pointage
                {
                    MatriculeSage = matricule, DatePointage = jour, HeurePointage = heure,
                    NomBddSage = "SAGE_TEST", NomBddPointeuse = "PTE_TEST"
                });
        }

        public void Ferie(DateTime jour) =>
            Uow.Get<JourFerie>().Items.Add(new JourFerie { Date = jour, BddSage = "SAGE_TEST" });

        /// <summary>
        /// Semaine du 17 au 21 novembre 2025, trois salariés.
        /// <list type="bullet">
        ///   <item>001 : planifié 8 h–17 h les 5 jours ; lundi 08:00 en double (doublon exact), mardi
        ///     08:00 puis +20 s (quasi-doublon), vendredi 08:00 puis +60 s (quasi-doublon seulement
        ///     à 120 s) ; mercredi et jeudi fériés planifiés sans correction (pointage mercredi, aucun jeudi) ;</item>
        ///   <item>002 : AUCUN planning, pointe le lundi (exclu par défaut) ;</item>
        ///   <item>003 : planifié le lundi seulement, pointe lundi et mardi (mardi sans planning : contrôlé).</item>
        /// </list>
        /// </summary>
        public void Semaine()
        {
            Carte(1, "001");
            Carte(2, "002");
            Carte(3, "003");
            for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
                Plan(1, jour);
            Plan(3, Lundi);
            Ferie(Mercredi);
            Ferie(Jeudi);

            Punch("001", Lundi, H8, H8, H17);
            Punch("001", Mardi, H8, Plus(H8, 20), H17);
            Punch("001", Mercredi, H8, H17);
            Punch("001", Vendredi, H8, Plus(H8, 60), H17);
            Punch("002", Lundi, H8, H17);
            Punch("003", Lundi, H8, H17);
            Punch("003", Mardi, H8, H17);
        }
    }

    private static ClosingAnalysisRequest Periode(ClosingRulesOptions? options = null, bool information = false)
        => new(Lundi, Vendredi, Options: options, IncludeInformation: information);

    private static int Compte(IReadOnlyList<ClosingCountDto> comptes, object cle) =>
        comptes.Single(c => c.Cle == cle.ToString()).Nombre;

    private static ClosingOptions Lus(params (string Cle, string Valeur)[] valeurs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(valeurs.Select(v => new KeyValuePair<string, string?>(v.Cle, v.Valeur)))
            .Build();
        var options = new ClosingOptions();
        new ClosingOptionsSetup(configuration).Configure(options);
        return options;
    }

    /// <summary>Réglages d'avant la phase 3c : tolérance 120 s, doublons Mineure, férié Importante, seuil 0,02.</summary>
    private static ClosingRulesOptions AnciensReglages() => new()
    {
        DuplicateToleranceSeconds = 120,
        ExclureSansPlanningSurPeriode = false,
        CriticalRatioBlocking = 0.02m,
        Gravites = new Dictionary<ClosingAnomalyType, ClosingSeverity>
        {
            [ClosingAnomalyType.DoublonExact] = ClosingSeverity.Mineure,
            [ClosingAnomalyType.QuasiDoublon] = ClosingSeverity.Mineure,
            [ClosingAnomalyType.FerieNonApplique] = ClosingSeverity.Importante
        }
    };

    // ---------- Étape 1 : niveau Information ----------

    [Fact]
    public void ClosingSeverity_Information_HasWeightZeroAndOthersKeepTheirValues()
    {
        ((int)ClosingSeverity.Information).Should().Be(0);
        new[] { (int)ClosingSeverity.Mineure, (int)ClosingSeverity.Importante, (int)ClosingSeverity.Critique }.Should().Equal(new[] { 1, 3, 5 });
        ClosingRulesOptions.Default.Poids(ClosingSeverity.Information).Should().Be(0);
        ClosingRulesOptions.Default.PoidsMaximum.Should().Be(5);
    }

    [Fact]
    public void Score_InformationAnomalies_AreIgnored()
    {
        // Arrange
        var information = Enumerable.Range(0, 40)
            .Select(i => new ClosingAnomaly(ClosingAnomalyType.DoublonExact, ClosingSeverity.Information, "1", Lundi.AddDays(i), "x"))
            .ToList();

        // Act
        var avecMineure = information.Append(new ClosingAnomaly(ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, "1", Lundi, "x")).ToList();

        // Assert
        PayrollClosingRules.Score(information, 10).Should().Be(100);
        // Une Mineure (1) sur 10 jours : 100 − 100 × 1 / 50 = 98, quel que soit le nombre d'Information.
        PayrollClosingRules.Score(avecMineure, 10).Should().Be(98);
    }

    [Fact]
    public void Statut_InformationAnomalies_AreIgnored()
    {
        // Act
        var information = Enumerable.Range(0, 50)
            .Select(_ => new ClosingAnomaly(ClosingAnomalyType.FerieNonApplique, ClosingSeverity.Information, "1", Lundi, "x"))
            .ToList();

        // Assert
        ClosingStatusRules.Statut(information, 10, 0.05m).Should().Be(ClosingStatus.Pret);
    }

    [Fact]
    public async Task AnalyserAsync_InformationAnomalies_AreCountedSeparately()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        // Act
        var resultat = await ctx.Service.AnalyserAsync(Periode());

        // Assert
        Compte(resultat.ParGravite, ClosingSeverity.Information).Should().Be(2);
        Compte(resultat.ParGravite, ClosingSeverity.Mineure).Should().Be(2);
        Compte(resultat.ParGravite, ClosingSeverity.Importante).Should().Be(1);
        Compte(resultat.ParGravite, ClosingSeverity.Critique).Should().Be(0);
        resultat.ParGravite.Count.Should().Be(Enum.GetValues<ClosingSeverity>().Length);
    }

    [Fact]
    public void Erreurs_InformationWeight_AcceptsPositiveAndRefusesNegative()
    {
        // Arrange
        var sut = Lus(("Closing:Poids:Information", "2"));

        // Act
        var result = sut.Erreurs();

        // Assert
        result.Should().BeEmpty();
        sut.ReglesParDefaut().Poids(ClosingSeverity.Information).Should().Be(2);
        var negatif = Lus(("Closing:Poids:Information", "-1"));
        var resultat = new ClosingOptionsValidator().Validate(null, negatif);
        resultat.Failed.Should().BeTrue();
        resultat.Failures!.Should().ContainSingle().Which.Should().Contain("Poids:Information");
    }

    // ---------- Étape 2 : doublons selon le mode ----------

    private static Pointage Punch(string matricule, DateTime jour, TimeSpan heure) =>
        new() { MatriculeSage = matricule, DatePointage = jour, HeurePointage = heure };

    private static readonly Pointage[] Doublons =
    {
        Punch("369", Lundi, H8), Punch("369", Lundi, H8), Punch("369", Lundi, Plus(H8, 10)), Punch("369", Lundi, H17)
    };

    [Fact]
    public void Duplicates_MultiPunchMode_AreInformation_MonoMode_AreMineure()
    {
        // Act
        var options = ClosingRulesOptions.Default;

        // Assert
        DuplicatePunchRules.DoublonsExacts(Doublons, new ClosingScope(Lundi, Vendredi, true, Options: options)).Should().AllSatisfy(a => a.Severity.Should().Be(ClosingSeverity.Information));
        DuplicatePunchRules.QuasiDoublons(Doublons, new ClosingScope(Lundi, Vendredi, true, Options: options)).Should().AllSatisfy(a => a.Severity.Should().Be(ClosingSeverity.Information));
        DuplicatePunchRules.DoublonsExacts(Doublons, new ClosingScope(Lundi, Vendredi, false, Options: options)).Should().AllSatisfy(a => a.Severity.Should().Be(ClosingSeverity.Mineure));
        DuplicatePunchRules.QuasiDoublons(Doublons, new ClosingScope(Lundi, Vendredi, false, Options: options)).Should().AllSatisfy(a => a.Severity.Should().Be(ClosingSeverity.Mineure));
        DuplicatePunchRules.QuasiDoublons(Doublons, new ClosingScope(Lundi, Vendredi, true, Options: options)).Should().NotBeEmpty();
    }

    [Fact]
    public void Duplicates_MonoSeverityIsConfigurable_AndAppliesOnlyInMono()
    {
        // Arrange
        var sut = new ClosingRulesOptions { DoublonsMonoGravite = ClosingSeverity.Importante };

        // Act & Assert
        sut.Gravite(ClosingAnomalyType.DoublonExact, multiPointage: false).Should().Be(ClosingSeverity.Importante);
        sut.Gravite(ClosingAnomalyType.QuasiDoublon, multiPointage: false).Should().Be(ClosingSeverity.Importante);
        sut.Gravite(ClosingAnomalyType.DoublonExact, multiPointage: true).Should().Be(ClosingSeverity.Information);
        DuplicatePunchRules.DoublonsExacts(Doublons, new ClosingScope(Lundi, Vendredi, false, Options: sut)).Should().AllSatisfy(a => a.Severity.Should().Be(ClosingSeverity.Importante));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicates_ExplicitSeverityInSettings_WinsInBothModes(bool multiPointage)
    {
        // Arrange
        var reglages = Lus(("Closing:Gravites:DoublonExact", "Critique"), ("Closing:Gravites:QuasiDoublon", "Importante"));
        var ctx = new Contexte(multiPointage, reglages);
        ctx.Semaine();

        // Act
        var resultat = await ctx.Service.AnalyserAsync(Periode(information: true));

        // Assert
        resultat.Anomalies.Items.Where(a => a.Type == nameof(ClosingAnomalyType.DoublonExact)).Should().AllSatisfy(a => a.Gravite.Should().Be("Critique"));
        resultat.Anomalies.Items.Where(a => a.Type == nameof(ClosingAnomalyType.QuasiDoublon)).Should().AllSatisfy(a => a.Gravite.Should().Be("Importante"));
        resultat.Anomalies.Items.Should().Contain(a => a.Type == nameof(ClosingAnomalyType.DoublonExact));
    }

    [Fact]
    public void Duplicates_ExplicitSeverityOfAnotherType_DoesNotOverrideModeRule()
    {
        // Arrange
        // Seul QuasiDoublon est fixé : DoublonExact garde la règle par mode (Information en multi-pointage).
        var options = new ClosingRulesOptions
        {
            Gravites = new Dictionary<ClosingAnomalyType, ClosingSeverity> { [ClosingAnomalyType.QuasiDoublon] = ClosingSeverity.Importante }
        };
        var exacts = DuplicatePunchRules.DoublonsExacts(Doublons, new ClosingScope(Lundi, Vendredi, true, Options: options));

        // Act
        var appliquees = PayrollClosingRules.AppliquerGravites(exacts, options);

        // Assert
        appliquees.Should().AllSatisfy(a => a.Severity.Should().Be(ClosingSeverity.Information));
    }

    [Fact]
    public void DoublonsExacts_Duplicate_DetailStatesThePayrollEffect()
    {
        // Act & Assert
        var exactMulti = DuplicatePunchRules.DoublonsExacts(Doublons, new ClosingScope(Lundi, Vendredi, true)).Should().ContainSingle().Which;
        var exactMono = DuplicatePunchRules.DoublonsExacts(Doublons, new ClosingScope(Lundi, Vendredi, false)).Should().ContainSingle().Which;
        var quasiMulti = DuplicatePunchRules.QuasiDoublons(Doublons, new ClosingScope(Lundi, Vendredi, true)).Should().ContainSingle().Which;
        var quasiMono = DuplicatePunchRules.QuasiDoublons(Doublons, new ClosingScope(Lundi, Vendredi, false)).Should().ContainSingle().Which;
        exactMulti.Detail.Should().Contain("double passage sans effet sur la paie (multi-pointage)");
        quasiMulti.Detail.Should().Contain("double passage sans effet sur la paie (multi-pointage)");
        exactMono.Detail.Should().Contain("peut fausser les heures");
        quasiMono.Detail.Should().Contain("peut fausser les heures");
    }

    // ---------- Étape 2.1 : tolérance de 30 s ----------

    [Theory]
    [InlineData(29, 1)]
    [InlineData(30, 0)]
    [InlineData(31, 0)]
    public void QuasiDoublons_DefaultTolerance_Is30Seconds(int secondes, int attendu)
    {
        var pointages = new[] { Punch("369", Lundi, H8), Punch("369", Lundi, Plus(H8, secondes)) };

        DuplicatePunchRules.QuasiDoublons(pointages, new ClosingScope(Lundi, Vendredi)).Count.Should().Be(attendu);
    }

    // ---------- Étape 3 : salariés sans planning ----------

    private static readonly CardPaie[] Cartes =
    {
        new() { Id = 1, SageMatricule = "001" }, new() { Id = 2, SageMatricule = "002" }, new() { Id = 3, SageMatricule = "003" }
    };

    private static PlanningSalarie Planning(int id, int idsal, DateTime jour, bool off = false) =>
        new() { Id = id, IdSal = idsal, DateP = jour, Ha = jour.Add(H8), Hd = off ? jour.Add(H8) : jour.Add(H17), OffP = off };

    private static readonly Pointage[] PointagesSansPlanning =
    {
        Punch("001", Lundi, H8), Punch("002", Lundi, H8), Punch("003", Lundi, H8), Punch("003", Mardi, H8)
    };

    [Fact]
    public void WithoutPlanning_EmployeeWithNoPlanningLineAtAll_IsExcludedByDefault()
    {
        // Arrange
        // 001 planifié lundi, 002 sans aucune ligne, 003 planifié lundi seulement (partiellement planifié).
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 3, Lundi) };

        // Act
        var resultat = PlanningCoverageRules.PointagesSansPlanning(Cartes, plannings, PointagesSansPlanning, new ClosingScope(Lundi, Vendredi));

        // Assert
        var anomalie = resultat.Should().ContainSingle().Which;
        anomalie.Matricule.Should().Be("3");
        anomalie.Date.Should().Be(Mardi);
    }

    [Fact]
    public void WithoutPlanning_OptionFalse_ReportsEmployeeWithoutAnyPlanning()
    {
        // Arrange
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 3, Lundi) };
        var options = new ClosingRulesOptions { ExclureSansPlanningSurPeriode = false };

        // Act
        var resultat = PlanningCoverageRules.PointagesSansPlanning(Cartes, plannings, PointagesSansPlanning, new ClosingScope(Lundi, Vendredi, Options: options));

        // Assert
        resultat.Select(a => (a.Matricule, a.Date)).Should().Equal(new[] { ("2", Lundi), ("3", Mardi) });
    }

    [Fact]
    public void WithoutPlanning_OffLineCountsAsPlanning_SoEmployeeIsNotExcluded()
    {
        // Arrange
        // 002 n'a qu'une ligne OFF un autre jour : « de toute nature » → contrôlé, son lundi pointé est signalé.
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 2, Mercredi, off: true) };

        // Act
        var resultat = PlanningCoverageRules.PointagesSansPlanning(Cartes, plannings, PointagesSansPlanning, new ClosingScope(Lundi, Vendredi));

        // Assert
        resultat.Should().Contain(a => a.Matricule == "2" && a.Date == Lundi);
    }

    [Fact]
    public void SalariesSansPlanningSurPeriode_PlanningOutsideThePeriod_DoesNotCount()
    {
        // Arrange
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 2, Lundi.AddDays(-14)), Planning(3, 3, Mardi) };
        var index = ClosingPunchIndex.Build(PointagesSansPlanning);

        // Act
        var exclus = PlanningCoverageRules.SalariesSansPlanningSurPeriode(Cartes, plannings, index, new ClosingScope(Lundi, Vendredi));

        // Assert
        exclus.Should().Contain("2");
        exclus.Should().NotContain("1");
        exclus.Should().NotContain("3");
    }

    [Fact]
    public async Task AnalyserAsync_EmployeesWithoutPlanning_ExposesExcludedCount()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();
        var parDefaut = await ctx.Service.AnalyserAsync(Periode());

        // Act
        var sansExclusion = await ctx.Service.AnalyserAsync(Periode(new ClosingRulesOptions { ExclureSansPlanningSurPeriode = false }));

        // Assert
        parDefaut.SalariesSansPlanningExclus.Should().Be(1);
        Compte(parDefaut.ParType, ClosingAnomalyType.PointageSansPlanning).Should().Be(1);
        sansExclusion.SalariesSansPlanningExclus.Should().Be(0);
        Compte(sansExclusion.ParType, ClosingAnomalyType.PointageSansPlanning).Should().Be(2);
        // Les jours de 002 restent dans les jours-employé contrôlés dans les deux cas : 5 + 1 + 2.
        parDefaut.JoursEmployeControles.Should().Be(8);
        sansExclusion.JoursEmployeControles.Should().Be(8);
    }

    // ---------- Étape 4 : férié non appliqué ----------

    [Fact]
    public async Task Holiday_IsInformation_WithNewDetail_AndPunchExistsFlag()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();
        var resultat = await ctx.Service.AnalyserAsync(Periode(information: true));

        // Act
        var feries = resultat.Anomalies.Items.Where(a => a.Type == nameof(ClosingAnomalyType.FerieNonApplique)).OrderBy(a => a.Date).ToList();

        // Assert
        feries.Count.Should().Be(2);
        feries.Should().AllSatisfy(a =>
        {
            a.Gravite.Should().Be("Information");
            a.Poids.Should().Be(0);
            a.Detail.Should().Be("drapeau férié absent (sans effet sur le calcul de la paie)");
        });
        feries[0].Date.Should().Be(Mercredi);
        feries[0].PointageExiste.Should().BeTrue();
        feries[1].Date.Should().Be(Jeudi);
        feries[1].PointageExiste.Should().BeFalse();
        resultat.Anomalies.Items.Where(a => a.Type != nameof(ClosingAnomalyType.FerieNonApplique)).Should().AllSatisfy(a => a.PointageExiste.Should().BeNull());
    }

    [Fact]
    public void Holiday_WithoutPunchIndex_LeavesPunchFlagUnknown()
    {
        // Arrange
        var feries = new HashSet<DateTime> { Mercredi };
        var plannings = new[] { Planning(1, 1, Mercredi) };
        var sans = HolidayPunchRules.FeriesNonAppliques(new EmployeeSources(Cartes, plannings, Array.Empty<HeureCorrigee>()), feries, new ReportPeriod(Lundi, Vendredi, new DateTime(2025, 12, 1)));

        // Act
        var avec = HolidayPunchRules.FeriesNonAppliques(new EmployeeSources(Cartes, plannings, Array.Empty<HeureCorrigee>()),
                                                        feries,
                                                        new ReportPeriod(Lundi, Vendredi, new DateTime(2025, 12, 1)),
                                                        ClosingPunchIndex.Build(new[] { Punch("001", Mercredi, H8) }));

        // Assert
        sans.Should().ContainSingle().Which.PointageExiste.Should().BeNull();
        avec.Should().ContainSingle().Which.PointageExiste.Should().BeTrue();
    }

    // ---------- Étape 5 : seuil ----------

    [Fact]
    public void CriticalRatioBlocking_Default_IsFivePercent()
    {
        ClosingStatusRules.DefaultCriticalRatioBlocking.Should().Be(0.05m);
        ClosingRulesOptions.Default.CriticalRatioBlocking.Should().Be(0.05m);
        new ClosingOptions().CriticalRatioBlocking.Should().Be(0.05m);
        // 4 critiques sur 100 jours (4 %) : à corriger ; 5 sur 100 (5 %) : bloquant.
        ClosingStatusRules.Statut(4, 100, ClosingRulesOptions.Default.CriticalRatioBlocking).Should().Be(ClosingStatus.ACorriger);
        ClosingStatusRules.Statut(5, 100, ClosingRulesOptions.Default.CriticalRatioBlocking).Should().Be(ClosingStatus.Bloquant);
    }

    // ---------- Étape 6 : includeInformation ----------

    [Fact]
    public async Task IncludeInformation_DefaultListOmitsInformation_ButCountsKeepIt()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();
        var masque = await ctx.Service.AnalyserAsync(Periode());

        // Act
        var visible = await ctx.Service.AnalyserAsync(Periode(information: true));

        // Assert
        // 1 doublon exact + 1 quasi + 2 fériés (Information) + 1 sans planning = 5 ; sans Information : 3.
        masque.NombreAnomalies.Should().Be(5);
        visible.NombreAnomalies.Should().Be(5);
        masque.Anomalies.Total.Should().Be(3);
        masque.Anomalies.Items.Should().NotContain(a => a.Gravite == "Information");
        visible.Anomalies.Total.Should().Be(5);
        visible.Anomalies.Items.Count(a => a.Gravite == "Information").Should().Be(2);
        Compte(masque.ParGravite, ClosingSeverity.Information).Should().Be(2);
        visible.Score.Should().Be(masque.Score);
        visible.Statut.Should().Be(masque.Statut);
    }

    [Fact]
    public async Task IncludeInformation_MultiPunchMode_DuplicatesAppearOnlyWhenRequested()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: true);
        ctx.Semaine();
        var masque = await ctx.Service.AnalyserAsync(Periode());

        // Act
        var visible = await ctx.Service.AnalyserAsync(Periode(information: true));

        // Assert
        masque.Anomalies.Items.Where(a => a.Type is "DoublonExact" or "QuasiDoublon").Should().BeEmpty();
        visible.Anomalies.Items.Should().Contain(a => a.Type == "DoublonExact" && a.Gravite == "Information");
        visible.Anomalies.Items.Should().Contain(a => a.Type == "QuasiDoublon" && a.Gravite == "Information");
        (Compte(masque.ParGravite, ClosingSeverity.Information) >= 4).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyserAsync_IncludeInformation_PaginationTotalFollowsDisplayedList()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        // Act
        var page = await ctx.Service.AnalyserAsync(Periode(information: true) with { PageSize = 2 });

        // Assert
        page.Anomalies.Total.Should().Be(5);
        page.Anomalies.TotalPages.Should().Be(3);
        page.Anomalies.Items.Count.Should().Be(2);
    }

    [Fact]
    public async Task PresenterAsync_Analysis_CarriesPunchFlagAndExcludedCount()
    {
        // Arrange
        var reglages = Lus(("Closing:ShowNames", "false"));
        var ctx = new Contexte(multiPointage: false, reglages);
        ctx.Semaine();
        var sut = new ClosingPresentationService(ctx.Uow, ctx.Tenant, Options.Create(reglages));
        var analyse = await ctx.Service.AnalyserAsync(Periode(information: true));

        // Act
        var rapport = await sut.PresenterAsync(analyse);

        // Assert
        rapport.SalariesSansPlanningExclus.Should().Be(1);
        rapport.SeuilBloquant.Should().Be(0.05m);
        var ferie = rapport.Anomalies.Items.First(a => a.Type == nameof(ClosingAnomalyType.FerieNonApplique));
        ferie.PointageExiste.Should().BeTrue();
    }

    // ---------- Réglages ----------

    [Fact]
    public void ReglesParDefaut_NewSettingKeys_AreReadAndPassedToRules()
    {
        // Arrange
        var sut = Lus(("Closing:DoublonsMonoGravite", "importante"), ("Closing:ExclureSansPlanningSurPeriode", "false"),
            ("Closing:DuplicateToleranceSeconds", "120"), ("Closing:Gravites:FerieNonApplique", "Importante"),
            ("Closing:Gravites:DoublonExact", "Information"));

        // Act
        var regles = sut.ReglesParDefaut();

        // Assert
        sut.Erreurs().Should().BeEmpty();
        regles.DoublonsMonoGravite.Should().Be(ClosingSeverity.Importante);
        regles.ExclureSansPlanningSurPeriode.Should().BeFalse();
        regles.DuplicateToleranceSeconds.Should().Be(120);
        regles.Gravite(ClosingAnomalyType.FerieNonApplique).Should().Be(ClosingSeverity.Importante);
        regles.Gravite(ClosingAnomalyType.DoublonExact, multiPointage: false).Should().Be(ClosingSeverity.Information);
    }

    [Theory]
    [InlineData("Closing:DoublonsMonoGravite", "Grave", "DoublonsMonoGravite")]
    [InlineData("Closing:DoublonsMonoGravite", "3", "DoublonsMonoGravite")]
    [InlineData("Closing:ExclureSansPlanningSurPeriode", "oui", "ExclureSansPlanningSurPeriode")]
    [InlineData("Closing:Poids:Information", "abc", "Poids:Information")]
    public void Validate_InvalidNewSettingValues_AreRefusedWithoutEchoingValue(string cle, string valeur, string attendu)
    {
        // Act
        var resultat = new ClosingOptionsValidator().Validate(null, Lus((cle, valeur)));

        // Assert
        resultat.Failed.Should().BeTrue();
        var message = resultat.Failures!.Should().ContainSingle().Which;
        message.Should().Contain(attendu);
        message.Replace(attendu, string.Empty).Should().NotContain(valeur);
    }

    // ---------- Non-régression : anciens réglages ----------

    [Fact]
    public async Task AnalyserAsync_OldSettings_ReproducePreviousBehaviourExactly()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        // Act
        var ancien = await ctx.Service.AnalyserAsync(Periode(AnciensReglages(), information: true));

        // Assert
        // Tolérance 120 s : les quasi-doublons à 20 s et à 60 s sont tous deux détectés.
        Compte(ancien.ParType, ClosingAnomalyType.DoublonExact).Should().Be(1);
        Compte(ancien.ParType, ClosingAnomalyType.QuasiDoublon).Should().Be(2);
        // Exclusion désactivée : 002 (lundi) et 003 (mardi) sont tous deux signalés.
        Compte(ancien.ParType, ClosingAnomalyType.PointageSansPlanning).Should().Be(2);
        Compte(ancien.ParType, ClosingAnomalyType.FerieNonApplique).Should().Be(2);
        ancien.SalariesSansPlanningExclus.Should().Be(0);
        // Gravités d'avant : 3 Mineures, 4 Importantes, aucune Information ni Critique.
        Compte(ancien.ParGravite, ClosingSeverity.Information).Should().Be(0);
        Compte(ancien.ParGravite, ClosingSeverity.Mineure).Should().Be(3);
        Compte(ancien.ParGravite, ClosingSeverity.Importante).Should().Be(4);
        Compte(ancien.ParGravite, ClosingSeverity.Critique).Should().Be(0);
        // La liste complète est visible sans demander les Information : 7 anomalies, 8 jours-employé.
        ancien.NombreAnomalies.Should().Be(7);
        ancien.Anomalies.Total.Should().Be(7);
        // Score : 100 − 100 × (3 × 1 + 4 × 3) / (5 × 8) = 62,5 → 63 ; aucune critique → Prêt.
        ancien.Score.Should().Be(63);
        ancien.Statut.Should().Be("Pret");
        ancien.Anomalies.Items.Should().AllSatisfy(a => a.Gravite.Should().NotBe("Information"));
    }

    [Fact]
    public async Task AnalyserAsync_OldSettings_ListIsIdenticalWithOrWithoutInformation()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();
        var sans = await ctx.Service.AnalyserAsync(Periode(AnciensReglages(), information: false));

        // Act
        var avec = await ctx.Service.AnalyserAsync(Periode(AnciensReglages(), information: true));

        // Assert
        sans.Anomalies.Items.Select(a => (a.Type, a.Gravite, a.Matricule, a.Date)).Should().Equal(avec.Anomalies.Items.Select(a => (a.Type, a.Gravite, a.Matricule, a.Date)));
    }

    [Fact]
    public async Task AnalyserAsync_NewDefaults_ChangeOnlyDecidedBehaviour()
    {
        // Arrange
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        // Act
        var nouveau = await ctx.Service.AnalyserAsync(Periode());

        // Assert
        Compte(nouveau.ParType, ClosingAnomalyType.QuasiDoublon).Should().Be(1);
        Compte(nouveau.ParType, ClosingAnomalyType.PointageSansPlanning).Should().Be(1);
        Compte(nouveau.ParGravite, ClosingSeverity.Information).Should().Be(2);
        // Score : 100 − 100 × (2 × 1 + 1 × 3) / (5 × 8) = 87,5 → 88.
        nouveau.Score.Should().Be(88);
    }
}
