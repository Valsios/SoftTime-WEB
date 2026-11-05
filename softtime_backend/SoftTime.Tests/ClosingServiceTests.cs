using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Analyse de clôture : orchestration des règles <c>PayrollClosingRules</c> sur une période, score
/// global et par département, pagination, comparaison avec la période précédente, lecture seule.
/// </summary>
public class ClosingServiceTests
{
    // Aujourd'hui = lundi 1er décembre 2025 : toute la semaine du 17 au 21 novembre est passée.
    private static readonly DateTime Today = new(2025, 12, 1);
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime Vendredi = new(2025, 11, 21);
    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Today.AddHours(10), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class HorlogeMobile : TimeProvider
    {
        private DateTimeOffset _maintenant = new(Today.AddHours(10), TimeSpan.Zero);
        public void Avancer(TimeSpan d) => _maintenant += d;
        public override DateTimeOffset GetUtcNow() => _maintenant;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>Doublure qui délègue à <see cref="InMemoryUnitOfWork"/> et compte toute écriture.</summary>
    private sealed class UowEspion : IUnitOfWork
    {
        public InMemoryUnitOfWork Inner { get; } = new();
        public int Ecritures { get; set; }

        public IRepository<T> Repository<T>() where T : class => new RepoEspion<T>(Inner.Get<T>(), this);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Ecritures++;
            return Inner.SaveChangesAsync(cancellationToken);
        }

        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default)
        {
            Ecritures++;
            return Inner.ExecuteSqlAsync(sql, cancellationToken);
        }
    }

    private sealed class RepoEspion<T> : IRepository<T> where T : class
    {
        private readonly InMemoryRepository<T> _inner;
        private readonly UowEspion _uow;

        public RepoEspion(InMemoryRepository<T> inner, UowEspion uow)
        {
            _inner = inner;
            _uow = uow;
        }

        public IQueryable<T> Query() => _inner.Query();
        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => _inner.ListAsync(predicate, cancellationToken);
        public Task<List<T>> ListLatestAsync(Expression<Func<T, bool>> predicate, Expression<Func<T, DateTime?>> orderByDescending, int take, CancellationToken cancellationToken = default)
            => _inner.ListLatestAsync(predicate, orderByDescending, take, cancellationToken);

        public Task AddAsync(T entity, CancellationToken cancellationToken = default) { _uow.Ecritures++; return _inner.AddAsync(entity, cancellationToken); }
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { _uow.Ecritures++; return _inner.AddRangeAsync(entities, cancellationToken); }
        public void Update(T entity) { _uow.Ecritures++; _inner.Update(entity); }
        public void Remove(T entity) { _uow.Ecritures++; _inner.Remove(entity); }
        public void RemoveRange(IEnumerable<T> entities) { _uow.Ecritures++; _inner.RemoveRange(entities); }
    }

    private sealed class Contexte
    {
        public UowEspion Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public ClosingService Service { get; }

        public Contexte(TimeProvider? horloge = null, ClosingOptions? reglages = null)
        {
            Uow.Inner.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Inner.Get<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1, MULTIPOINT = false });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            Service = new ClosingService(Uow, tenant, new SageLeaveCalendar(tenant, Source), horloge ?? new HorlogeFixe(), reglages);
        }

        public void Carte(int id, string matricule, string? departement)
        {
            Uow.Inner.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = id, SAGE_MATRICULE = matricule, DEPARTEMENT = departement, SAGE_BDD = "SAGE_TEST", POINTEUSE_BDD = "PTE_TEST"
            });
            Source.Employees.Add(new ExternalEmployee(matricule, id, "X", "Y", null, false));
        }

        public void Plan(int idsal, DateTime jour, TimeSpan? ha = null, TimeSpan? hd = null)
            => Uow.Inner.Get<T_PLANNING_SAL>().Items.Add(new T_PLANNING_SAL
            {
                ID = Uow.Inner.Get<T_PLANNING_SAL>().Items.Count + 1,
                IDSAL = idsal,
                DateP = jour.Date,
                HA = jour.Date.Add(ha ?? H8),
                HD = jour.Date.Add(hd ?? H17),
                OffP = false,
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            });

        public void Punch(string matricule, DateTime jour, params TimeSpan[] heures)
        {
            foreach (var heure in heures)
                Uow.Inner.Get<T_POINTAGE>().Items.Add(new T_POINTAGE
                {
                    MATRICULE_SAGE = matricule, DATE_POINTAGE = jour.Date, HEURE_POINTAGE = heure,
                    NOM_BDD_SAGE = "SAGE_TEST", NOM_BDD_POINTEUSE = "PTE_TEST"
                });
        }

        public void Conge(int idsal, DateTime jour)
        {
            if (Source.Events.Count == 0)
                Source.Events.Add(new ExternalEvent("CP", "Congé payé"));
            Source.EmployeeEvents.Add(new ExternalEmployeeEvent(idsal, "CP", jour.Date, jour.Date, null, null));
        }

        public void SemaineValidee(string matricule, DateTime lundi)
            => Uow.Inner.Get<T_HS_VALIDER>().Items.Add(new T_HS_VALIDER
            {
                BDD_SAGE = "SAGE_TEST", MATRICULES = matricule, DATE_DEB = lundi, DATE_FIN = lundi.AddDays(6), VALIDATION_HS = true
            });

        public void Ferie(DateTime jour) =>
            Uow.Inner.Get<T_FERIE>().Items.Add(new T_FERIE { DATE = jour, BDD_SAGE = "SAGE_TEST" });

        /// <summary>
        /// Jeu fixe, semaine du lundi 17 au vendredi 21 novembre 2025, deux salariés planifiés 8 h–17 h :
        /// <list type="bullet">
        ///   <item>001 (INFO) : mardi un seul pointage (incomplet, 5), mercredi 08:00 en double
        ///     (doublon exact, 1), jeudi 08:00 puis 08:00:20 (quasi-doublon, 1) ;</item>
        ///   <item>002 (RH) : lundi sans pointage (manquant, 5), le reste complet.</item>
        /// </list>
        /// </summary>
        public void JeuFixe()
        {
            Carte(1, "001", "INFO");
            Carte(2, "002", "RH");
            for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
            {
                Plan(1, jour);
                Plan(2, jour);
            }

            Punch("001", Lundi, H8, H17);
            Punch("001", Lundi.AddDays(1), H8);
            Punch("001", Lundi.AddDays(2), H8, H8, H17);
            Punch("001", Lundi.AddDays(3), H8, H8.Add(TimeSpan.FromSeconds(20)), H17);
            Punch("001", Vendredi, H8, H17);
            for (var jour = Lundi.AddDays(1); jour <= Vendredi; jour = jour.AddDays(1))
                Punch("002", jour, H8, H17);
        }

        /// <summary>Semaine précédente (mercredi 12 au dimanche 16) : 3 jours planifiés pour chacun.</summary>
        public void SemainePrecedente(bool manquantsPour002)
        {
            for (var jour = new DateTime(2025, 11, 12); jour <= new DateTime(2025, 11, 14); jour = jour.AddDays(1))
            {
                Plan(1, jour);
                Plan(2, jour);
                Punch("001", jour, H8, H17);
                if (!manquantsPour002 || jour == new DateTime(2025, 11, 14))
                    Punch("002", jour, H8, H17);
            }
        }
    }

    private static ClosingAnalysisRequest Semaine(string? departement = null, int? page = null, int? taille = null, ClosingRulesOptions? options = null)
        => new(Lundi, Vendredi, departement, page, taille, options);

    private static int Compte(IReadOnlyList<ClosingCountDto> comptes, object cle) =>
        comptes.Single(c => c.Cle == cle.ToString()).Nombre;

    [Fact]
    public async Task Analyse_FixedSet_ScoreAndCountsMatchHandComputation()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());

        // Poids 5 + 1 + 1 + 5 = 12 sur 10 jours-employé : 100 − 100 × 12 / (5 × 10) = 76
        Assert.Equal(10, result.JoursEmployeControles);
        Assert.Equal(76, result.Score);
        Assert.Equal(4, result.NombreAnomalies);
        Assert.Equal(PayrollClosingRules.FormuleScore, result.Formule);
        Assert.Equal(2, Compte(result.ParGravite, ClosingSeverity.Critique));
        Assert.Equal(0, Compte(result.ParGravite, ClosingSeverity.Importante));
        Assert.Equal(2, Compte(result.ParGravite, ClosingSeverity.Mineure));
        Assert.Equal(1, Compte(result.ParType, ClosingAnomalyType.PointageIncomplet));
        Assert.Equal(1, Compte(result.ParType, ClosingAnomalyType.PointageManquant));
        Assert.Equal(1, Compte(result.ParType, ClosingAnomalyType.DoublonExact));
        Assert.Equal(1, Compte(result.ParType, ClosingAnomalyType.QuasiDoublon));
        Assert.Equal(0, Compte(result.ParType, ClosingAnomalyType.PointageSansPlanning));
        Assert.Equal(Enum.GetValues<ClosingAnomalyType>().Length, result.ParType.Count);
    }

    [Fact]
    public async Task Analyse_FixedSet_ScorePerDepartment()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());

        // INFO : 5 + 1 + 1 = 7 sur 5 jours → 72 ; RH : 5 sur 5 jours → 80
        Assert.Collection(result.ParDepartement,
            info =>
            {
                Assert.Equal("INFO", info.Departement);
                Assert.Equal(5, info.JoursEmployeControles);
                Assert.Equal(72, info.Score);
                Assert.Equal(3, info.NombreAnomalies);
            },
            rh =>
            {
                Assert.Equal("RH", rh.Departement);
                Assert.Equal(5, rh.JoursEmployeControles);
                Assert.Equal(80, rh.Score);
                Assert.Equal(1, Compte(rh.ParType, ClosingAnomalyType.PointageManquant));
            });
    }

    [Fact]
    public async Task Analyse_ExposesMatriculeAndDepartmentOnly()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("\"X\"", json);
        Assert.DoesNotContain("\"Y\"", json);
        Assert.Contains(result.Anomalies.Items, a => a.Matricule == "2" && a.Departement == "RH");
    }

    [Fact]
    public async Task Analyse_DepartmentFilter_KeepsOnlyThatDepartment()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine(departement: "rh"));

        Assert.Equal("rh", result.Departement);
        Assert.Equal(5, result.JoursEmployeControles);
        Assert.Equal(80, result.Score);
        var anomalie = Assert.Single(result.Anomalies.Items);
        Assert.Equal(nameof(ClosingAnomalyType.PointageManquant), anomalie.Type);
        Assert.Equal("RH", Assert.Single(result.ParDepartement).Departement);
    }

    [Fact]
    public async Task Analyse_PeriodWithoutData_ScoreAndComparisonAreNull()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(new DateTime(2025, 10, 1), new DateTime(2025, 10, 5)));

        Assert.Equal(0, result.JoursEmployeControles);
        Assert.Null(result.Score);
        Assert.Null(result.Comparaison);
        Assert.Empty(result.Anomalies.Items);
        Assert.Equal(1, result.Anomalies.Page);
    }

    [Fact]
    public async Task Analyse_FuturePeriod_IsNeverControlled()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001", "INFO");
        ctx.Plan(1, Today);
        ctx.Plan(1, Today.AddDays(1));
        ctx.Punch("001", Today, H8);

        var result = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Today, Today.AddDays(1)));

        Assert.Equal(0, result.JoursEmployeControles);
        Assert.Null(result.Score);
        Assert.Equal(0, result.NombreAnomalies);
    }

    [Fact]
    public async Task Analyse_PreviousPeriodWithoutData_ComparisonIsNull()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());

        Assert.Equal(76, result.Score);
        Assert.Null(result.Comparaison);
    }

    [Fact]
    public async Task Analyse_Comparison_Improvement()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var result = await ctx.Service.AnalyserAsync(Semaine() with { IncludeComparison = true });

        // Précédente (12 → 16 nov.) : 2 manquants sur 6 jours → 100 − 100 × 10 / 30 = 66,67 → 67
        var comparaison = Assert.IsType<ClosingComparisonDto>(result.Comparaison);
        Assert.Equal(new DateTime(2025, 11, 12), comparaison.From);
        Assert.Equal(new DateTime(2025, 11, 16), comparaison.To);
        Assert.Equal(6, comparaison.JoursEmployeControlesPrecedent);
        Assert.Equal(67, comparaison.ScorePrecedent);
        Assert.Equal(9, comparaison.EcartScore);
        var manquants = comparaison.ParType.Single(t => t.Type == nameof(ClosingAnomalyType.PointageManquant));
        Assert.Equal((2, 1, -1), (manquants.Precedent, manquants.Actuel, manquants.Ecart));
        var exacts = comparaison.ParType.Single(t => t.Type == nameof(ClosingAnomalyType.DoublonExact));
        Assert.Equal((0, 1, 1), (exacts.Precedent, exacts.Actuel, exacts.Ecart));
    }

    [Fact]
    public async Task Analyse_Comparison_Degradation()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: false);

        var result = await ctx.Service.AnalyserAsync(Semaine() with { IncludeComparison = true });

        var comparaison = Assert.IsType<ClosingComparisonDto>(result.Comparaison);
        Assert.Equal(100, comparaison.ScorePrecedent);
        Assert.Equal(-24, comparaison.EcartScore);
        Assert.All(comparaison.ParType, t => Assert.Equal(t.Actuel - t.Precedent, t.Ecart));
    }

    [Fact]
    public async Task Analyse_Pagination_Page2WithStableOrderAndTotal()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var complet = await ctx.Service.AnalyserAsync(Semaine(page: 1, taille: 100));
        var page2 = await ctx.Service.AnalyserAsync(Semaine(page: 2, taille: 3));

        Assert.Equal(
            new[]
            {
                nameof(ClosingAnomalyType.PointageManquant), nameof(ClosingAnomalyType.PointageIncomplet),
                nameof(ClosingAnomalyType.DoublonExact), nameof(ClosingAnomalyType.QuasiDoublon)
            },
            complet.Anomalies.Items.Select(a => a.Type));
        Assert.Equal(4, page2.Anomalies.Total);
        Assert.Equal(2, page2.Anomalies.Page);
        Assert.Equal(3, page2.Anomalies.PageSize);
        Assert.Equal(2, page2.Anomalies.TotalPages);
        Assert.Equal(complet.Anomalies.Items[3], Assert.Single(page2.Anomalies.Items));
    }

    [Fact]
    public async Task Analyse_FromAfterTo_ThrowsValidationError()
    {
        var ctx = new Contexte();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Vendredi, Lundi)));
    }

    [Fact]
    public async Task Analyse_PeriodLongerThan366Days_ThrowsValidationError()
    {
        var ctx = new Contexte();
        var from = new DateTime(2024, 1, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(from, from.AddDays(366))));
        ClosingService.Valider(new ClosingAnalysisRequest(from, from.AddDays(365)));
    }

    [Fact]
    public async Task Analyse_PauseShift_NeverRaisesAnomaly()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001", "INFO");
        ctx.Ferie(Lundi.AddDays(1));
        for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
            ctx.Plan(1, jour, TimeSpan.FromHours(12), TimeSpan.FromHours(12));

        var result = await ctx.Service.AnalyserAsync(Semaine());

        Assert.Equal(0, result.NombreAnomalies);
        Assert.Equal(0, result.JoursEmployeControles);
        Assert.Null(result.Score);
    }

    [Fact]
    public async Task Analyse_SageLeave_ExcludesMissingPunch()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.Conge(2, Lundi);

        var result = await ctx.Service.AnalyserAsync(Semaine());

        Assert.Equal(0, Compte(result.ParType, ClosingAnomalyType.PointageManquant));
    }

    [Fact]
    public async Task Analyse_ValidatedWeek_FollowsIgnoreOption()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemaineValidee("001", Lundi);

        var parDefaut = await ctx.Service.AnalyserAsync(Semaine());
        var signalees = await ctx.Service.AnalyserAsync(Semaine(options: new ClosingRulesOptions { IgnorerSemainesValidees = false }));

        Assert.Equal(0, Compte(parDefaut.ParType, ClosingAnomalyType.PointageIncomplet));
        Assert.Equal(1, Compte(signalees.ParType, ClosingAnomalyType.PointageIncomplet));
    }

    [Fact]
    public async Task Analyse_TwoCalls_IdenticalResult()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var premier = JsonSerializer.Serialize(await ctx.Service.AnalyserAsync(Semaine()));
        var second = JsonSerializer.Serialize(await ctx.Service.AnalyserAsync(Semaine()));

        Assert.Equal(premier, second);
    }

    [Fact]
    public async Task Analyse_ComparisonNotRequested_IsAbsentByDefault()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var result = await ctx.Service.AnalyserAsync(Semaine());

        Assert.False(Semaine().IncludeComparison);
        Assert.Null(result.Comparaison);
        Assert.Equal(76, result.Score);
        Assert.Equal(4, result.NombreAnomalies);
    }

    [Fact]
    public async Task Analyse_ComparisonNotRequested_PreviousPeriodIsNotLoaded()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var sans = await ctx.Service.ChargerAsync(Semaine(), CancellationToken.None);
        var avec = await ctx.Service.ChargerAsync(Semaine() with { IncludeComparison = true }, CancellationToken.None);

        Assert.DoesNotContain(sans.Pointages, p => p.DATE_POINTAGE < Lundi);
        Assert.DoesNotContain(sans.Plannings, p => p.DateP < Lundi.AddDays(-1));
        Assert.Contains(avec.Pointages, p => p.DATE_POINTAGE < Lundi);
    }

    [Fact]
    public async Task Analyse_WithAndWithoutComparison_SameCurrentPeriodResult()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var sans = await ctx.Service.AnalyserAsync(Semaine());
        var avec = await ctx.Service.AnalyserAsync(Semaine() with { IncludeComparison = true });

        Assert.NotNull(avec.Comparaison);
        Assert.Equal(
            JsonSerializer.Serialize(sans),
            JsonSerializer.Serialize(avec with { Comparaison = null }));
    }

    [Fact]
    public async Task Analyse_NoPeriod_DefaultsToPreviousCalendarMonth()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(default, default));

        // Aujourd'hui = 1er décembre 2025 : mois civil complet précédent = novembre 2025.
        Assert.Equal(new DateTime(2025, 11, 1), result.From);
        Assert.Equal(new DateTime(2025, 11, 30), result.To);
        Assert.Equal(76, result.Score);
    }

    [Theory]
    [InlineData(2025, 12, 1, 2025, 11, 1, 2025, 11, 30)]
    [InlineData(2025, 12, 31, 2025, 11, 1, 2025, 11, 30)]
    [InlineData(2026, 1, 15, 2025, 12, 1, 2025, 12, 31)]
    [InlineData(2024, 3, 10, 2024, 2, 1, 2024, 2, 29)]
    public void DefaultPeriod_IsPreviousCalendarMonth(int a, int m, int j, int a1, int m1, int j1, int a2, int m2, int j2)
    {
        var request = ClosingService.AvecPeriodeParDefaut(new ClosingAnalysisRequest(default, default), new DateTime(a, m, j));

        Assert.Equal(new DateTime(a1, m1, j1), request.From);
        Assert.Equal(new DateTime(a2, m2, j2), request.To);
    }

    [Fact]
    public async Task Analyse_OnlyOneBoundProvided_ThrowsValidationError()
    {
        var ctx = new Contexte();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, default)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(default, Vendredi)));
    }

    [Fact]
    public async Task Analyse_NeverWrites()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        await ctx.Service.AnalyserAsync(Semaine());
        await ctx.Service.AnalyserAsync(Semaine(departement: "INFO", page: 2, taille: 1));

        Assert.Equal(0, ctx.Uow.Ecritures);
    }

    // ----------------------------------------------------------------------------- cache de l'analyse

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static ClosingAnalysisRequest Page(int page, int taille, bool reuse, bool information = false, string? departement = null, bool comparaison = false)
        => new(Lundi, Vendredi, departement, page, taille, IncludeInformation: information, IncludeComparison: comparaison, ReuseCached: reuse);

    [Fact]
    public async Task Cache_ResultIdenticalWithAndWithoutCache_ForEveryPageAndFilter()
    {
        var avec = new Contexte();
        avec.JeuFixe();
        avec.SemainePrecedente(manquantsPour002: true);
        var sans = new Contexte(); // même données, cache jamais réutilisé
        sans.JeuFixe();
        sans.SemainePrecedente(manquantsPour002: true);

        // Première demande : remplit le cache ; les suivantes (changements de page) le réutilisent.
        await avec.Service.AnalyserAsync(Page(1, 1, reuse: false, comparaison: true));
        foreach (var info in new[] { false, true })
            foreach (var comparaison in new[] { true })
                for (var page = 1; page <= 5; page++)
                    foreach (var taille in new[] { 1, 2, 50 })
                    {
                        var attendu = await sans.Service.AnalyserAsync(Page(page, taille, reuse: false, info, comparaison: comparaison));
                        var obtenu = await avec.Service.AnalyserAsync(Page(page, taille, reuse: true, info, comparaison: comparaison));
                        Assert.Equal(Json(attendu), Json(obtenu));
                    }
    }

    [Fact]
    public async Task Cache_WithoutReuseRequest_AlwaysRecomputesFromData()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        var avant = await ctx.Service.AnalyserAsync(Page(1, 50, reuse: false));

        ctx.Punch("002", Lundi, H8, H17); // la donnée change : le manquant du lundi disparaît
        var recalcule = await ctx.Service.AnalyserAsync(Page(1, 50, reuse: false));
        var reutilise = await ctx.Service.AnalyserAsync(Page(1, 50, reuse: true));

        Assert.Equal(4, avant.NombreAnomalies);
        Assert.Equal(3, recalcule.NombreAnomalies);   // nouvelle analyse : toujours à jour
        Assert.Equal(3, reutilise.NombreAnomalies);   // l'analyse précédente a remplacé l'entrée du cache
    }

    [Fact]
    public async Task Cache_ReuseServesStoredAnalysis_ThenExpires()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        await ctx.Service.AnalyserAsync(Page(1, 50, reuse: false));
        ctx.Punch("002", Lundi, H8, H17);

        Assert.Equal(4, (await ctx.Service.AnalyserAsync(Page(2, 1, reuse: true))).NombreAnomalies); // dans les 120 s : analyse conservée
        horloge.Avancer(TimeSpan.FromSeconds(121));
        Assert.Equal(3, (await ctx.Service.AnalyserAsync(Page(2, 1, reuse: true))).NombreAnomalies); // expirée : recalculée
    }

    [Fact]
    public async Task Cache_KeyIncludesDepartmentComparisonAndRules()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);
        await ctx.Service.AnalyserAsync(Page(1, 50, reuse: false));

        var info = await ctx.Service.AnalyserAsync(Page(1, 50, reuse: true, departement: "INFO"));
        var rh = await ctx.Service.AnalyserAsync(Page(1, 50, reuse: true, departement: "RH"));
        var avecComparaison = await ctx.Service.AnalyserAsync(Page(1, 50, reuse: true, comparaison: true));
        var autresPoids = await ctx.Service.AnalyserAsync(
            new ClosingAnalysisRequest(Lundi, Vendredi, Options: new ClosingRulesOptions { PoidsCritique = 10 }, ReuseCached: true));

        Assert.Equal("INFO", info.Departement);
        Assert.Equal(3, info.NombreAnomalies);
        Assert.Equal(1, rh.NombreAnomalies);
        Assert.NotNull(avecComparaison.Comparaison);
        Assert.NotEqual((await ctx.Service.AnalyserAsync(Page(1, 50, reuse: true))).Score, autresPoids.Score);
    }

    // ----------------------------------------------------------------------------- filtre et tri par gravité

    /// <summary>
    /// Gravités réglées pour que l'ordre par date diffère de l'ordre par gravité :
    /// lundi manquant (Mineure), mardi incomplet (Critique), mercredi doublon exact (Information), jeudi quasi-doublon (Critique).
    /// </summary>
    private static ClosingRulesOptions GravitesMelangees() => new()
    {
        Gravites = new Dictionary<ClosingAnomalyType, ClosingSeverity>
        {
            [ClosingAnomalyType.PointageManquant] = ClosingSeverity.Mineure,
            [ClosingAnomalyType.DoublonExact] = ClosingSeverity.Information,
            [ClosingAnomalyType.QuasiDoublon] = ClosingSeverity.Critique,
        }
    };

    private static ClosingAnalysisRequest AvecGravite(
        IReadOnlyList<ClosingSeverity>? niveaux = null, bool tri = false, bool information = false, int? page = null, int? taille = null, bool reuse = false)
        => new(Lundi, Vendredi, null, page, taille, GravitesMelangees(), IncludeInformation: information, ReuseCached: reuse, Gravites: niveaux, TriParGravite: tri);

    private static string[] Types(ClosingAnalysisDto a) => a.Anomalies.Items.Select(x => x.Type).ToArray();

    /// <summary>Tout sauf la liste : ce qui ne doit jamais dépendre du filtre ni du tri.</summary>
    private static string SansListe(ClosingAnalysisDto a) =>
        Json(new { a.Score, a.Statut, a.NombreAnomalies, a.JoursEmployeControles, a.ParGravite, a.ParType, a.ParDepartement, a.Comparaison, a.From, a.To });

    [Fact]
    public async Task Gravite_WithoutParameter_IsStrictlyIdenticalToTheDefaultOutput()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var absent = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Vendredi, null, 1, 50, IncludeComparison: true));
        var explicite = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Vendredi, null, 1, 50, IncludeComparison: true,
            Gravites: null, TriParGravite: false));
        var videExplicite = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Vendredi, null, 1, 50, IncludeComparison: true,
            Gravites: Array.Empty<ClosingSeverity>(), TriParGravite: false));

        Assert.Equal(Json(absent), Json(explicite));
        Assert.Equal(Json(absent), Json(videExplicite));
        // Ordre historique : date, matricule, type (le lundi manquant, puis incomplet, doublon exact, quasi-doublon).
        Assert.Equal(new[] { "PointageManquant", "PointageIncomplet", "DoublonExact", "QuasiDoublon" }, Types(absent));
        Assert.Equal(76, absent.Score);
        Assert.Equal(4, absent.NombreAnomalies);
        Assert.Equal(4, absent.Anomalies.Total);
    }

    [Fact]
    public async Task Gravite_Filter_KeepsScoreStatusAndCountsOnTheFullAnalysis()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var complet = await ctx.Service.AnalyserAsync(AvecGravite(information: true));
        foreach (var niveaux in new[] { new[] { ClosingSeverity.Critique }, new[] { ClosingSeverity.Mineure, ClosingSeverity.Information }, new[] { ClosingSeverity.Importante } })
        {
            var filtre = await ctx.Service.AnalyserAsync(AvecGravite(niveaux));
            Assert.Equal(SansListe(complet), SansListe(filtre));   // score, statut, comptages, départements : inchangés
            Assert.All(filtre.Anomalies.Items, a => Assert.Contains(Enum.Parse<ClosingSeverity>(a.Gravite), niveaux));
        }
    }

    [Fact]
    public async Task Gravite_Filter_RestrictsTheListAndTheTotalButNotTheCounts()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var critiques = await ctx.Service.AnalyserAsync(AvecGravite(new[] { ClosingSeverity.Critique }));
        Assert.Equal(new[] { "PointageIncomplet", "QuasiDoublon" }, Types(critiques));
        Assert.Equal(2, critiques.Anomalies.Total);
        Assert.Equal(4, critiques.NombreAnomalies);

        var deux = await ctx.Service.AnalyserAsync(AvecGravite(new[] { ClosingSeverity.Critique, ClosingSeverity.Mineure }));
        Assert.Equal(new[] { "PointageManquant", "PointageIncomplet", "QuasiDoublon" }, Types(deux));   // ordre habituel conservé

        var aucune = await ctx.Service.AnalyserAsync(AvecGravite(new[] { ClosingSeverity.Importante }));
        Assert.Empty(aucune.Anomalies.Items);
        Assert.Equal(0, aucune.Anomalies.Total);
        Assert.Equal(4, aucune.NombreAnomalies);
    }

    [Fact]
    public async Task Gravite_ExplicitFilter_OverridesIncludeInformation()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var parDefaut = await ctx.Service.AnalyserAsync(AvecGravite());                           // Information masquée
        var avecInfo = await ctx.Service.AnalyserAsync(AvecGravite(information: true));
        var infoSeule = await ctx.Service.AnalyserAsync(AvecGravite(new[] { ClosingSeverity.Information }));       // demandée par le filtre
        var critiquesEtInfo = await ctx.Service.AnalyserAsync(AvecGravite(new[] { ClosingSeverity.Critique }, information: true));

        Assert.DoesNotContain("DoublonExact", Types(parDefaut));
        Assert.Contains("DoublonExact", Types(avecInfo));
        Assert.Equal(new[] { "DoublonExact" }, Types(infoSeule));
        Assert.DoesNotContain("DoublonExact", Types(critiquesEtInfo));   // le filtre explicite l'emporte
    }

    [Fact]
    public async Task Gravite_Sort_PutsTheMostSevereFirstThenKeepsTheUsualOrderStable()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var trie = await ctx.Service.AnalyserAsync(AvecGravite(tri: true, information: true));
        var graviteDe = trie.Anomalies.Items.Select(a => Enum.Parse<ClosingSeverity>(a.Gravite)).ToArray();

        Assert.Equal(new[] { ClosingSeverity.Critique, ClosingSeverity.Critique, ClosingSeverity.Mineure, ClosingSeverity.Information }, graviteDe);
        // Égalités départagées par l'ordre habituel (date, matricule, type) : incomplet (mardi) avant quasi-doublon (jeudi).
        Assert.Equal(new[] { "PointageIncomplet", "QuasiDoublon", "PointageManquant", "DoublonExact" }, Types(trie));
        // Déterministe : mêmes demandes, mêmes résultats.
        Assert.Equal(Json(trie), Json(await ctx.Service.AnalyserAsync(AvecGravite(tri: true, information: true))));
        // Le tri ne change ni le total ni les comptages ni le score.
        var sansTri = await ctx.Service.AnalyserAsync(AvecGravite(information: true));
        Assert.Equal(sansTri.Anomalies.Total, trie.Anomalies.Total);
        Assert.Equal(SansListe(sansTri), SansListe(trie));
        Assert.Equal(Types(sansTri).OrderBy(t => t), Types(trie).OrderBy(t => t));   // mêmes lignes, autre ordre
    }

    [Fact]
    public async Task Gravite_SortedPages_ConcatenateToTheFullSortedListWithoutGapsOrDuplicates()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        var complet = await ctx.Service.AnalyserAsync(AvecGravite(tri: true, information: true, page: 1, taille: 50));

        var assemble = new List<ClosingAnomalyDto>();
        for (var page = 1; page <= 4; page++)
            assemble.AddRange((await ctx.Service.AnalyserAsync(AvecGravite(tri: true, information: true, page: page, taille: 1))).Anomalies.Items);

        Assert.Equal(Json(complet.Anomalies.Items), Json(assemble));
    }

    [Fact]
    public async Task Gravite_FilterAndSort_ServedFromCacheEqualFreshComputation_ForEveryPage()
    {
        var avec = new Contexte();
        avec.JeuFixe();
        var sans = new Contexte();
        sans.JeuFixe();
        await avec.Service.AnalyserAsync(AvecGravite(page: 1, taille: 1));   // remplit le cache

        foreach (var niveaux in new IReadOnlyList<ClosingSeverity>?[] { null, new[] { ClosingSeverity.Critique }, new[] { ClosingSeverity.Mineure, ClosingSeverity.Critique } })
            foreach (var tri in new[] { false, true })
                for (var page = 1; page <= 4; page++)
                {
                    var attendu = await sans.Service.AnalyserAsync(AvecGravite(niveaux, tri, true, page, 1, reuse: false));
                    var obtenu = await avec.Service.AnalyserAsync(AvecGravite(niveaux, tri, true, page, 1, reuse: true));
                    Assert.Equal(Json(attendu), Json(obtenu));
                }
    }

    [Fact]
    public async Task Gravite_FilterAndSort_NeverWrite()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        await ctx.Service.AnalyserAsync(AvecGravite(new[] { ClosingSeverity.Critique }, tri: true, information: true));

        Assert.Equal(0, ctx.Uow.Ecritures);
    }

    // ----------------------------------------------------------------------------- cache glissant sous plafond absolu

    private static readonly TimeSpan Pas = TimeSpan.FromSeconds(50);   // plus court que le délai glissant (120 s)

    private static async Task<int> Anomalies(Contexte ctx, bool reuse, string? perimetre = null)
        => (await ctx.Service.AnalyserAsync(Page(1, 50, reuse) with { Perimetre = perimetre })).NombreAnomalies;

    [Fact]
    public async Task CacheSliding_StaysValidWhileReadingContinuously_ThenExpiresAtTheAbsoluteCeiling()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        Assert.Equal(4, await Anomalies(ctx, reuse: false));         // calcul d'origine : t = 0
        ctx.Punch("002", Lundi, H8, H17);                            // la donnée change : un recalcul donnerait 3

        for (var i = 1; i <= 11; i++)                                // lectures toutes les 50 s jusqu'à t = 550 s (délai glissant 120 s)
        {
            horloge.Avancer(Pas);
            Assert.Equal(4, await Anomalies(ctx, reuse: true));
        }
        horloge.Avancer(TimeSpan.FromSeconds(49));                   // t = 599 s : sous le plafond de 600 s
        Assert.Equal(4, await Anomalies(ctx, reuse: true));
        horloge.Avancer(TimeSpan.FromSeconds(2));                    // t = 601 s : plafond dépassé malgré la lecture continue
        Assert.Equal(3, await Anomalies(ctx, reuse: true));
    }

    [Fact]
    public async Task CacheSliding_WithoutReading_StillExpiresAfterTheSlidingDelay()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        await Anomalies(ctx, reuse: false);
        ctx.Punch("002", Lundi, H8, H17);

        horloge.Avancer(Pas);
        Assert.Equal(4, await Anomalies(ctx, reuse: true));          // lue à 50 s : renouvelée jusqu'à 110 s
        horloge.Avancer(TimeSpan.FromSeconds(121));                  // 171 s : plus de 120 s sans lecture depuis celle de 50 s
        Assert.Equal(3, await Anomalies(ctx, reuse: true));
    }

    [Fact]
    public async Task CacheSliding_AnalysisWithoutReuse_AlwaysRecomputesAndRestartsTheCeiling()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        await Anomalies(ctx, reuse: false);                          // t = 0
        ctx.Punch("002", Lundi, H8, H17);
        for (var i = 0; i < 11; i++)
        {
            horloge.Avancer(Pas);
            await Anomalies(ctx, reuse: true);                       // lectures continues jusqu'à t = 550 s
        }
        horloge.Avancer(TimeSpan.FromSeconds(40));                   // t = 590 s : ancien plafond (600 s) presque atteint

        Assert.Equal(3, await Anomalies(ctx, reuse: false));         // « Analyser » : recalcule toujours à neuf, plafond neuf (jusqu'à 1190 s)
        ctx.Punch("001", Lundi.AddDays(1), H17);                     // nouvelle donnée : un recalcul donnerait 2

        horloge.Avancer(TimeSpan.FromSeconds(50));                   // t = 640 s : l'ancien plafond est dépassé depuis longtemps
        Assert.Equal(3, await Anomalies(ctx, reuse: true));          // l'entrée neuve est toujours servie
        for (var i = 0; i < 9; i++)
        {
            horloge.Avancer(Pas);
            await Anomalies(ctx, reuse: true);                       // jusqu'à t = 1090 s
        }
        horloge.Avancer(Pas);                                        // t = 1140 s
        Assert.Equal(3, await Anomalies(ctx, reuse: true));
        horloge.Avancer(TimeSpan.FromSeconds(49));                   // t = 1189 s : sous le plafond neuf (590 + 600 = 1190 s)
        Assert.Equal(3, await Anomalies(ctx, reuse: true));
        horloge.Avancer(TimeSpan.FromSeconds(2));                    // t = 1191 s : plafond neuf dépassé malgré la lecture continue
        Assert.Equal(2, await Anomalies(ctx, reuse: true));
    }

    [Fact]
    public async Task CacheSliding_TwoUsersNeverShareAnEntry()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        Assert.Equal(4, await Anomalies(ctx, reuse: false, perimetre: "u1"));
        ctx.Punch("002", Lundi, H8, H17);

        Assert.Equal(4, await Anomalies(ctx, reuse: true, perimetre: "u1"));   // l'entrée de u1
        Assert.Equal(3, await Anomalies(ctx, reuse: true, perimetre: "u2"));   // u2 ne lit jamais l'entrée de u1 : calcul à neuf
        Assert.Equal(3, await Anomalies(ctx, reuse: true, perimetre: "u2"));   // puis sa propre entrée
        Assert.Equal(4, await Anomalies(ctx, reuse: true, perimetre: "u1"));   // celle de u1 reste inchangée
        Assert.Equal(3, await Anomalies(ctx, reuse: true));                    // sans périmètre : encore une autre clé
    }

    [Fact]
    public async Task CacheSliding_ResultIsIdenticalWithAndWithoutTheCache_AfterRenewals()
    {
        var horloge = new HorlogeMobile();
        var avec = new Contexte(horloge);
        avec.JeuFixe();
        avec.SemainePrecedente(manquantsPour002: true);
        var sans = new Contexte();
        sans.JeuFixe();
        sans.SemainePrecedente(manquantsPour002: true);
        await avec.Service.AnalyserAsync(Page(1, 1, reuse: false, comparaison: true));
        foreach (var page in new[] { 1, 2, 3, 4 })
        {
            horloge.Avancer(Pas);                                    // une lecture toutes les 50 s : l'entrée est renouvelée à chaque page
            var attendu = await sans.Service.AnalyserAsync(Page(page, 1, reuse: false, information: true, comparaison: true));
            var obtenu = await avec.Service.AnalyserAsync(Page(page, 1, reuse: true, information: true, comparaison: true));
            Assert.Equal(Json(attendu), Json(obtenu));
        }
    }

    // ----------------------------------------------------------------------------- délai glissant de 120 s

    [Fact]
    public async Task CacheSliding120_IsolatedClickAt90Seconds_IsServedByTheCache()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        Assert.Equal(4, await Anomalies(ctx, reuse: false));         // analyse : t = 0
        ctx.Punch("002", Lundi, H8, H17);                            // un recalcul donnerait 3

        horloge.Avancer(TimeSpan.FromSeconds(90));                   // clic isolé, sans lecture entre-temps
        Assert.Equal(4, await Anomalies(ctx, reuse: true));          // servi par le cache
    }

    [Fact]
    public async Task CacheSliding120_ClickAt130SecondsWithoutAnyRead_Recomputes()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        await Anomalies(ctx, reuse: false);
        ctx.Punch("002", Lundi, H8, H17);

        horloge.Avancer(TimeSpan.FromSeconds(130));                  // plus de 120 s sans lecture
        Assert.Equal(3, await Anomalies(ctx, reuse: true));
    }

    [Fact]
    public async Task CacheSliding120_ConfiguredDelayIsUsed()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge, new ClosingOptions { CacheSeconds = 30 });
        ctx.JeuFixe();
        await Anomalies(ctx, reuse: false);
        ctx.Punch("002", Lundi, H8, H17);

        horloge.Avancer(TimeSpan.FromSeconds(25));
        Assert.Equal(4, await Anomalies(ctx, reuse: true));          // dans les 30 s configurées
        horloge.Avancer(TimeSpan.FromSeconds(31));
        Assert.Equal(3, await Anomalies(ctx, reuse: true));          // 31 s sans lecture : expirée
    }

    [Fact]
    public async Task CacheSliding120_SlidingDelayNeverExceedsTheCeiling()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge, new ClosingOptions { CacheSeconds = 120, CacheMaxSeconds = 100 });
        ctx.JeuFixe();
        await Anomalies(ctx, reuse: false);
        ctx.Punch("002", Lundi, H8, H17);

        horloge.Avancer(TimeSpan.FromSeconds(99));
        Assert.Equal(4, await Anomalies(ctx, reuse: true));
        horloge.Avancer(TimeSpan.FromSeconds(2));                    // t = 101 s : le plafond (100 s) l'emporte sur les 120 s glissantes
        Assert.Equal(3, await Anomalies(ctx, reuse: true));
    }
}
