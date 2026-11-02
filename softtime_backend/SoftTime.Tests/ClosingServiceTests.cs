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

        public Contexte()
        {
            Uow.Inner.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Inner.Get<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1, MULTIPOINT = false });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            Service = new ClosingService(Uow, tenant, new SageLeaveCalendar(tenant, Source), new HorlogeFixe());
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
}
