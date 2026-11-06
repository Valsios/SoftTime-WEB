using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

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
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1, MULTIPOINT = multiPointage });
            Tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            Service = new ClosingService(Uow, Tenant, new SageLeaveCalendar(Tenant, new FakeExternalSourceService()),
                new HorlogeFixe(), reglages ?? new ClosingOptions());
        }

        public void Carte(int id, string matricule) =>
            Uow.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = id, SAGE_MATRICULE = matricule, DEPARTEMENT = "D", SAGE_BDD = "SAGE_TEST", POINTEUSE_BDD = "PTE_TEST"
            });

        public void Plan(int idsal, DateTime jour, bool off = false) =>
            Uow.Get<T_PLANNING_SAL>().Items.Add(new T_PLANNING_SAL
            {
                ID = Uow.Get<T_PLANNING_SAL>().Items.Count + 1, IDSAL = idsal, DateP = jour,
                HA = jour.Add(H8), HD = jour.Add(H17), OffP = off, BDD_SAGE = "SAGE_TEST", BDD_POINTEUSE = "PTE_TEST"
            });

        public void Punch(string matricule, DateTime jour, params TimeSpan[] heures)
        {
            foreach (var heure in heures)
                Uow.Get<T_POINTAGE>().Items.Add(new T_POINTAGE
                {
                    MATRICULE_SAGE = matricule, DATE_POINTAGE = jour, HEURE_POINTAGE = heure,
                    NOM_BDD_SAGE = "SAGE_TEST", NOM_BDD_POINTEUSE = "PTE_TEST"
                });
        }

        public void Ferie(DateTime jour) =>
            Uow.Get<T_FERIE>().Items.Add(new T_FERIE { DATE = jour, BDD_SAGE = "SAGE_TEST" });

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
        options.Lire(configuration.GetSection(ClosingOptions.Section));
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
    public void Information_HasWeightZeroAndOtherSeveritiesKeepTheirValues()
    {
        Assert.Equal(0, (int)ClosingSeverity.Information);
        Assert.Equal(new[] { 1, 3, 5 }, new[] { (int)ClosingSeverity.Mineure, (int)ClosingSeverity.Importante, (int)ClosingSeverity.Critique });
        Assert.Equal(0, ClosingRulesOptions.Default.Poids(ClosingSeverity.Information));
        Assert.Equal(5, ClosingRulesOptions.Default.PoidsMaximum);
    }

    [Fact]
    public void Information_NeverEntersScore()
    {
        var information = Enumerable.Range(0, 40)
            .Select(i => new ClosingAnomaly(ClosingAnomalyType.DoublonExact, ClosingSeverity.Information, "1", Lundi.AddDays(i), "x"))
            .ToList();
        var avecMineure = information.Append(new ClosingAnomaly(ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, "1", Lundi, "x")).ToList();

        Assert.Equal(100, PayrollClosingRules.Score(information, 10));
        // Une Mineure (1) sur 10 jours : 100 − 100 × 1 / 50 = 98, quel que soit le nombre d'Information.
        Assert.Equal(98, PayrollClosingRules.Score(avecMineure, 10));
    }

    [Fact]
    public void Information_NeverEntersStatus()
    {
        var information = Enumerable.Range(0, 50)
            .Select(_ => new ClosingAnomaly(ClosingAnomalyType.FerieNonApplique, ClosingSeverity.Information, "1", Lundi, "x"))
            .ToList();

        Assert.Equal(ClosingStatus.Pret, ClosingStatusRules.Statut(information, 10, 0.05m));
    }

    [Fact]
    public async Task Information_IsCountedSeparatelyInSeverityCounts()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var resultat = await ctx.Service.AnalyserAsync(Periode());

        Assert.Equal(2, Compte(resultat.ParGravite, ClosingSeverity.Information));      // les deux fériés
        Assert.Equal(2, Compte(resultat.ParGravite, ClosingSeverity.Mineure));          // doublon exact + quasi (20 s)
        Assert.Equal(1, Compte(resultat.ParGravite, ClosingSeverity.Importante));       // pointage sans planning (003, mardi)
        Assert.Equal(0, Compte(resultat.ParGravite, ClosingSeverity.Critique));
        Assert.Equal(Enum.GetValues<ClosingSeverity>().Length, resultat.ParGravite.Count);
    }

    [Fact]
    public void InformationWeight_IsAdjustableButNegativeIsRefused()
    {
        var reglable = Lus(("Closing:Poids:Information", "2"));
        Assert.Empty(reglable.Erreurs());
        Assert.Equal(2, reglable.ReglesParDefaut().Poids(ClosingSeverity.Information));

        var negatif = Lus(("Closing:Poids:Information", "-1"));
        var resultat = new ClosingOptionsValidator().Validate(null, negatif);
        Assert.True(resultat.Failed);
        Assert.Contains("Poids:Information", Assert.Single(resultat.Failures!));
    }

    // ---------- Étape 2 : doublons selon le mode ----------

    private static T_POINTAGE Punch(string matricule, DateTime jour, TimeSpan heure) =>
        new() { MATRICULE_SAGE = matricule, DATE_POINTAGE = jour, HEURE_POINTAGE = heure };

    private static readonly T_POINTAGE[] Doublons =
    {
        Punch("369", Lundi, H8), Punch("369", Lundi, H8), Punch("369", Lundi, Plus(H8, 10)), Punch("369", Lundi, H17)
    };

    [Fact]
    public void Duplicates_MultiPunchMode_AreInformation_MonoMode_AreMineure()
    {
        var options = ClosingRulesOptions.Default;

        Assert.All(PayrollClosingRules.DoublonsExacts(Doublons, Lundi, Vendredi, options, multiPointage: true),
            a => Assert.Equal(ClosingSeverity.Information, a.Severity));
        Assert.All(PayrollClosingRules.QuasiDoublons(Doublons, Lundi, Vendredi, options, multiPointage: true),
            a => Assert.Equal(ClosingSeverity.Information, a.Severity));
        Assert.All(PayrollClosingRules.DoublonsExacts(Doublons, Lundi, Vendredi, options, multiPointage: false),
            a => Assert.Equal(ClosingSeverity.Mineure, a.Severity));
        Assert.All(PayrollClosingRules.QuasiDoublons(Doublons, Lundi, Vendredi, options, multiPointage: false),
            a => Assert.Equal(ClosingSeverity.Mineure, a.Severity));
        Assert.NotEmpty(PayrollClosingRules.QuasiDoublons(Doublons, Lundi, Vendredi, options, multiPointage: true));
    }

    [Fact]
    public void Duplicates_MonoSeverityIsConfigurable_AndAppliesOnlyInMono()
    {
        var options = new ClosingRulesOptions { DoublonsMonoGravite = ClosingSeverity.Importante };

        Assert.Equal(ClosingSeverity.Importante, options.Gravite(ClosingAnomalyType.DoublonExact, multiPointage: false));
        Assert.Equal(ClosingSeverity.Importante, options.Gravite(ClosingAnomalyType.QuasiDoublon, multiPointage: false));
        Assert.Equal(ClosingSeverity.Information, options.Gravite(ClosingAnomalyType.DoublonExact, multiPointage: true));
        Assert.All(PayrollClosingRules.DoublonsExacts(Doublons, Lundi, Vendredi, options, multiPointage: false),
            a => Assert.Equal(ClosingSeverity.Importante, a.Severity));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicates_ExplicitSeverityInSettings_WinsInBothModes(bool multiPointage)
    {
        var reglages = Lus(("Closing:Gravites:DoublonExact", "Critique"), ("Closing:Gravites:QuasiDoublon", "Importante"));
        var ctx = new Contexte(multiPointage, reglages);
        ctx.Semaine();

        var resultat = await ctx.Service.AnalyserAsync(Periode(information: true));

        Assert.All(resultat.Anomalies.Items.Where(a => a.Type == nameof(ClosingAnomalyType.DoublonExact)),
            a => Assert.Equal("Critique", a.Gravite));
        Assert.All(resultat.Anomalies.Items.Where(a => a.Type == nameof(ClosingAnomalyType.QuasiDoublon)),
            a => Assert.Equal("Importante", a.Gravite));
        Assert.Contains(resultat.Anomalies.Items, a => a.Type == nameof(ClosingAnomalyType.DoublonExact));
    }

    [Fact]
    public void Duplicates_ExplicitSeverityOfAnotherType_DoesNotOverrideModeRule()
    {
        // Seul QuasiDoublon est fixé : DoublonExact garde la règle par mode (Information en multi-pointage).
        var options = new ClosingRulesOptions
        {
            Gravites = new Dictionary<ClosingAnomalyType, ClosingSeverity> { [ClosingAnomalyType.QuasiDoublon] = ClosingSeverity.Importante }
        };
        var exacts = PayrollClosingRules.DoublonsExacts(Doublons, Lundi, Vendredi, options, multiPointage: true);

        var appliquees = PayrollClosingRules.AppliquerGravites(exacts, options);

        Assert.All(appliquees, a => Assert.Equal(ClosingSeverity.Information, a.Severity));
    }

    [Fact]
    public void Duplicates_DetailTextStatesTheEffectOnPayroll()
    {
        var exactMulti = Assert.Single(PayrollClosingRules.DoublonsExacts(Doublons, Lundi, Vendredi, multiPointage: true));
        var exactMono = Assert.Single(PayrollClosingRules.DoublonsExacts(Doublons, Lundi, Vendredi, multiPointage: false));
        var quasiMulti = Assert.Single(PayrollClosingRules.QuasiDoublons(Doublons, Lundi, Vendredi, multiPointage: true));
        var quasiMono = Assert.Single(PayrollClosingRules.QuasiDoublons(Doublons, Lundi, Vendredi, multiPointage: false));

        Assert.Contains("double passage sans effet sur la paie (multi-pointage)", exactMulti.Detail);
        Assert.Contains("double passage sans effet sur la paie (multi-pointage)", quasiMulti.Detail);
        Assert.Contains("peut fausser les heures", exactMono.Detail);
        Assert.Contains("peut fausser les heures", quasiMono.Detail);
    }

    // ---------- Étape 2.1 : tolérance de 30 s ----------

    [Theory]
    [InlineData(29, 1)]
    [InlineData(30, 0)]
    [InlineData(31, 0)]
    public void NearDuplicate_DefaultToleranceIs30Seconds(int secondes, int attendu)
    {
        var pointages = new[] { Punch("369", Lundi, H8), Punch("369", Lundi, Plus(H8, secondes)) };

        Assert.Equal(attendu, PayrollClosingRules.QuasiDoublons(pointages, Lundi, Vendredi).Count);
    }

    // ---------- Étape 3 : salariés sans planning ----------

    private static readonly T_CARDPAIE[] Cartes =
    {
        new() { ID = 1, SAGE_MATRICULE = "001" }, new() { ID = 2, SAGE_MATRICULE = "002" }, new() { ID = 3, SAGE_MATRICULE = "003" }
    };

    private static T_PLANNING_SAL Planning(int id, int idsal, DateTime jour, bool off = false) =>
        new() { ID = id, IDSAL = idsal, DateP = jour, HA = jour.Add(H8), HD = off ? jour.Add(H8) : jour.Add(H17), OffP = off };

    private static readonly T_POINTAGE[] PointagesSansPlanning =
    {
        Punch("001", Lundi, H8), Punch("002", Lundi, H8), Punch("003", Lundi, H8), Punch("003", Mardi, H8)
    };

    [Fact]
    public void WithoutPlanning_EmployeeWithNoPlanningLineAtAll_IsExcludedByDefault()
    {
        // 001 planifié lundi, 002 sans aucune ligne, 003 planifié lundi seulement (partiellement planifié).
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 3, Lundi) };

        var resultat = PayrollClosingRules.PointagesSansPlanning(Cartes, plannings, PointagesSansPlanning, Lundi, Vendredi);

        var anomalie = Assert.Single(resultat);
        Assert.Equal("3", anomalie.Matricule);          // salarié partiellement planifié : PAS exclu
        Assert.Equal(Mardi, anomalie.Date);
    }

    [Fact]
    public void WithoutPlanning_OptionFalse_ReportsEmployeeWithoutAnyPlanning()
    {
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 3, Lundi) };
        var options = new ClosingRulesOptions { ExclureSansPlanningSurPeriode = false };

        var resultat = PayrollClosingRules.PointagesSansPlanning(Cartes, plannings, PointagesSansPlanning, Lundi, Vendredi, options);

        Assert.Equal(new[] { ("2", Lundi), ("3", Mardi) }, resultat.Select(a => (a.Matricule, a.Date)));
    }

    [Fact]
    public void WithoutPlanning_OffLineCountsAsPlanning_SoEmployeeIsNotExcluded()
    {
        // 002 n'a qu'une ligne OFF un autre jour : « de toute nature » → contrôlé, son lundi pointé est signalé.
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 2, Mercredi, off: true) };

        var resultat = PayrollClosingRules.PointagesSansPlanning(Cartes, plannings, PointagesSansPlanning, Lundi, Vendredi);

        Assert.Contains(resultat, a => a.Matricule == "2" && a.Date == Lundi);
    }

    [Fact]
    public void WithoutPlanning_PlanningOutsideThePeriodDoesNotCount()
    {
        var plannings = new[] { Planning(1, 1, Lundi), Planning(2, 2, Lundi.AddDays(-14)), Planning(3, 3, Mardi) };
        var index = ClosingPunchIndex.Build(PointagesSansPlanning);

        var exclus = PayrollClosingRules.SalariesSansPlanningSurPeriode(Cartes, plannings, index, Lundi, Vendredi);

        Assert.Contains("2", exclus);
        Assert.DoesNotContain("1", exclus);
        Assert.DoesNotContain("3", exclus);
    }

    [Fact]
    public async Task WithoutPlanning_AnalysisExposesTheNumberOfExcludedEmployees()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var parDefaut = await ctx.Service.AnalyserAsync(Periode());
        var sansExclusion = await ctx.Service.AnalyserAsync(Periode(new ClosingRulesOptions { ExclureSansPlanningSurPeriode = false }));

        Assert.Equal(1, parDefaut.SalariesSansPlanningExclus);                      // 002
        Assert.Equal(1, Compte(parDefaut.ParType, ClosingAnomalyType.PointageSansPlanning));
        Assert.Equal(0, sansExclusion.SalariesSansPlanningExclus);
        Assert.Equal(2, Compte(sansExclusion.ParType, ClosingAnomalyType.PointageSansPlanning));
        // Les jours de 002 restent dans les jours-employé contrôlés dans les deux cas : 5 + 1 + 2.
        Assert.Equal(8, parDefaut.JoursEmployeControles);
        Assert.Equal(8, sansExclusion.JoursEmployeControles);
    }

    // ---------- Étape 4 : férié non appliqué ----------

    [Fact]
    public async Task Holiday_IsInformation_WithNewDetail_AndPunchExistsFlag()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var resultat = await ctx.Service.AnalyserAsync(Periode(information: true));
        var feries = resultat.Anomalies.Items.Where(a => a.Type == nameof(ClosingAnomalyType.FerieNonApplique)).OrderBy(a => a.Date).ToList();

        Assert.Equal(2, feries.Count);
        Assert.All(feries, a =>
        {
            Assert.Equal("Information", a.Gravite);
            Assert.Equal(0, a.Poids);
            Assert.Equal("drapeau férié absent (sans effet sur le calcul de la paie)", a.Detail);
        });
        Assert.Equal(Mercredi, feries[0].Date);
        Assert.True(feries[0].PointageExiste);                                       // pointage le mercredi
        Assert.Equal(Jeudi, feries[1].Date);
        Assert.False(feries[1].PointageExiste);                                      // aucun pointage le jeudi
        Assert.All(resultat.Anomalies.Items.Where(a => a.Type != nameof(ClosingAnomalyType.FerieNonApplique)),
            a => Assert.Null(a.PointageExiste));
    }

    [Fact]
    public void Holiday_WithoutPunchIndex_LeavesPunchFlagUnknown()
    {
        var feries = new HashSet<DateTime> { Mercredi };
        var plannings = new[] { Planning(1, 1, Mercredi) };

        var sans = PayrollClosingRules.FeriesNonAppliques(Cartes, plannings, Array.Empty<T_HEURECORRIGER>(), feries, Lundi, Vendredi, new DateTime(2025, 12, 1));
        var avec = PayrollClosingRules.FeriesNonAppliques(Cartes, plannings, Array.Empty<T_HEURECORRIGER>(), feries, Lundi, Vendredi, new DateTime(2025, 12, 1),
            ClosingPunchIndex.Build(new[] { Punch("001", Mercredi, H8) }));

        Assert.Null(Assert.Single(sans).PointageExiste);
        Assert.True(Assert.Single(avec).PointageExiste);
    }

    // ---------- Étape 5 : seuil ----------

    [Fact]
    public void BlockingThreshold_DefaultIsFivePercent()
    {
        Assert.Equal(0.05m, ClosingStatusRules.DefaultCriticalRatioBlocking);
        Assert.Equal(0.05m, ClosingRulesOptions.Default.CriticalRatioBlocking);
        Assert.Equal(0.05m, new ClosingOptions().CriticalRatioBlocking);
        // 4 critiques sur 100 jours (4 %) : à corriger ; 5 sur 100 (5 %) : bloquant.
        Assert.Equal(ClosingStatus.ACorriger, ClosingStatusRules.Statut(4, 100, ClosingRulesOptions.Default.CriticalRatioBlocking));
        Assert.Equal(ClosingStatus.Bloquant, ClosingStatusRules.Statut(5, 100, ClosingRulesOptions.Default.CriticalRatioBlocking));
    }

    // ---------- Étape 6 : includeInformation ----------

    [Fact]
    public async Task IncludeInformation_DefaultListOmitsInformation_ButCountsKeepIt()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var masque = await ctx.Service.AnalyserAsync(Periode());
        var visible = await ctx.Service.AnalyserAsync(Periode(information: true));

        // 1 doublon exact + 1 quasi + 2 fériés (Information) + 1 sans planning = 5 ; sans Information : 3.
        Assert.Equal(5, masque.NombreAnomalies);
        Assert.Equal(5, visible.NombreAnomalies);
        Assert.Equal(3, masque.Anomalies.Total);
        Assert.DoesNotContain(masque.Anomalies.Items, a => a.Gravite == "Information");
        Assert.Equal(5, visible.Anomalies.Total);
        Assert.Equal(2, visible.Anomalies.Items.Count(a => a.Gravite == "Information"));
        Assert.Equal(2, Compte(masque.ParGravite, ClosingSeverity.Information));
        Assert.Equal(masque.Score, visible.Score);
        Assert.Equal(masque.Statut, visible.Statut);
    }

    [Fact]
    public async Task IncludeInformation_MultiPunchMode_DuplicatesAppearOnlyWhenRequested()
    {
        var ctx = new Contexte(multiPointage: true);
        ctx.Semaine();

        var masque = await ctx.Service.AnalyserAsync(Periode());
        var visible = await ctx.Service.AnalyserAsync(Periode(information: true));

        Assert.DoesNotContain(masque.Anomalies.Items, a => a.Type is "DoublonExact" or "QuasiDoublon");
        Assert.Contains(visible.Anomalies.Items, a => a.Type == "DoublonExact" && a.Gravite == "Information");
        Assert.Contains(visible.Anomalies.Items, a => a.Type == "QuasiDoublon" && a.Gravite == "Information");
        Assert.True(Compte(masque.ParGravite, ClosingSeverity.Information) >= 4);
    }

    [Fact]
    public async Task IncludeInformation_PaginationTotalFollowsTheDisplayedList()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var page = await ctx.Service.AnalyserAsync(Periode(information: true) with { PageSize = 2 });

        Assert.Equal(5, page.Anomalies.Total);
        Assert.Equal(3, page.Anomalies.TotalPages);
        Assert.Equal(2, page.Anomalies.Items.Count);
    }

    [Fact]
    public async Task Presentation_CarriesPunchFlagAndExcludedCount()
    {
        var reglages = Lus(("Closing:ShowNames", "false"));
        var ctx = new Contexte(multiPointage: false, reglages);
        ctx.Semaine();
        var presentation = new ClosingPresentationService(ctx.Uow, ctx.Tenant, reglages);

        var analyse = await ctx.Service.AnalyserAsync(Periode(information: true));
        var rapport = await presentation.PresenterAsync(analyse);

        Assert.Equal(1, rapport.SalariesSansPlanningExclus);
        Assert.Equal(0.05m, rapport.SeuilBloquant);
        var ferie = rapport.Anomalies.Items.First(a => a.Type == nameof(ClosingAnomalyType.FerieNonApplique));
        Assert.True(ferie.PointageExiste);
    }

    // ---------- Réglages ----------

    [Fact]
    public void Settings_NewKeysAreReadAndPassedToTheRules()
    {
        var o = Lus(("Closing:DoublonsMonoGravite", "importante"), ("Closing:ExclureSansPlanningSurPeriode", "false"),
            ("Closing:DuplicateToleranceSeconds", "120"), ("Closing:Gravites:FerieNonApplique", "Importante"),
            ("Closing:Gravites:DoublonExact", "Information"));
        var regles = o.ReglesParDefaut();

        Assert.Empty(o.Erreurs());
        Assert.Equal(ClosingSeverity.Importante, regles.DoublonsMonoGravite);
        Assert.False(regles.ExclureSansPlanningSurPeriode);
        Assert.Equal(120, regles.DuplicateToleranceSeconds);
        Assert.Equal(ClosingSeverity.Importante, regles.Gravite(ClosingAnomalyType.FerieNonApplique));
        Assert.Equal(ClosingSeverity.Information, regles.Gravite(ClosingAnomalyType.DoublonExact, multiPointage: false));
    }

    [Theory]
    [InlineData("Closing:DoublonsMonoGravite", "Grave", "DoublonsMonoGravite")]
    [InlineData("Closing:DoublonsMonoGravite", "3", "DoublonsMonoGravite")]
    [InlineData("Closing:ExclureSansPlanningSurPeriode", "oui", "ExclureSansPlanningSurPeriode")]
    [InlineData("Closing:Poids:Information", "abc", "Poids:Information")]
    public void Settings_InvalidNewValuesAreRefusedWithoutEchoingTheValue(string cle, string valeur, string attendu)
    {
        var resultat = new ClosingOptionsValidator().Validate(null, Lus((cle, valeur)));

        Assert.True(resultat.Failed);
        var message = Assert.Single(resultat.Failures!);
        Assert.Contains(attendu, message);
        Assert.DoesNotContain(valeur, message.Replace(attendu, string.Empty));
    }

    // ---------- Non-régression : anciens réglages ----------

    [Fact]
    public async Task OldSettings_ReproduceThePreviousBehaviourExactly()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var ancien = await ctx.Service.AnalyserAsync(Periode(AnciensReglages(), information: true));

        // Tolérance 120 s : les quasi-doublons à 20 s et à 60 s sont tous deux détectés.
        Assert.Equal(1, Compte(ancien.ParType, ClosingAnomalyType.DoublonExact));
        Assert.Equal(2, Compte(ancien.ParType, ClosingAnomalyType.QuasiDoublon));
        // Exclusion désactivée : 002 (lundi) et 003 (mardi) sont tous deux signalés.
        Assert.Equal(2, Compte(ancien.ParType, ClosingAnomalyType.PointageSansPlanning));
        Assert.Equal(2, Compte(ancien.ParType, ClosingAnomalyType.FerieNonApplique));
        Assert.Equal(0, ancien.SalariesSansPlanningExclus);
        // Gravités d'avant : 3 Mineures, 4 Importantes, aucune Information ni Critique.
        Assert.Equal(0, Compte(ancien.ParGravite, ClosingSeverity.Information));
        Assert.Equal(3, Compte(ancien.ParGravite, ClosingSeverity.Mineure));
        Assert.Equal(4, Compte(ancien.ParGravite, ClosingSeverity.Importante));
        Assert.Equal(0, Compte(ancien.ParGravite, ClosingSeverity.Critique));
        // La liste complète est visible sans demander les Information : 7 anomalies, 8 jours-employé.
        Assert.Equal(7, ancien.NombreAnomalies);
        Assert.Equal(7, ancien.Anomalies.Total);
        // Score : 100 − 100 × (3 × 1 + 4 × 3) / (5 × 8) = 62,5 → 63 ; aucune critique → Prêt.
        Assert.Equal(63, ancien.Score);
        Assert.Equal("Pret", ancien.Statut);
        Assert.All(ancien.Anomalies.Items, a => Assert.NotEqual("Information", a.Gravite));
    }

    [Fact]
    public async Task OldSettings_ListIsIdenticalWhetherInformationIsRequestedOrNot()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var sans = await ctx.Service.AnalyserAsync(Periode(AnciensReglages(), information: false));
        var avec = await ctx.Service.AnalyserAsync(Periode(AnciensReglages(), information: true));

        Assert.Equal(avec.Anomalies.Items.Select(a => (a.Type, a.Gravite, a.Matricule, a.Date)),
            sans.Anomalies.Items.Select(a => (a.Type, a.Gravite, a.Matricule, a.Date)));
    }

    [Fact]
    public async Task NewDefaults_ChangeOnlyWhatThePhaseDecided()
    {
        var ctx = new Contexte(multiPointage: false);
        ctx.Semaine();

        var nouveau = await ctx.Service.AnalyserAsync(Periode());

        Assert.Equal(1, Compte(nouveau.ParType, ClosingAnomalyType.QuasiDoublon));           // 20 s oui, 60 s non
        Assert.Equal(1, Compte(nouveau.ParType, ClosingAnomalyType.PointageSansPlanning));   // 002 exclu
        Assert.Equal(2, Compte(nouveau.ParGravite, ClosingSeverity.Information));            // fériés
        // Score : 100 − 100 × (2 × 1 + 1 × 3) / (5 × 8) = 87,5 → 88.
        Assert.Equal(88, nouveau.Score);
    }
}
