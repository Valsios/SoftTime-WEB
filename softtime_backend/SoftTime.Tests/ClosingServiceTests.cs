using System.Linq.Expressions;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services;

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

        public Contexte(TimeProvider? horloge = null)
        {
            Uow.Inner.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Inner.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = false });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            Service = new ClosingService(Uow, tenant, new SageLeaveCalendar(tenant, Source), horloge ?? new HorlogeFixe());
        }

        public void Carte(int id, string matricule, string? departement)
        {
            Uow.Inner.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = id, SageMatricule = matricule, Departement = departement, SageBdd = "SAGE_TEST", PointeuseBdd = "PTE_TEST"
            });
            Source.Employees.Add(new ExternalEmployee(matricule, id, "X", "Y", null, false));
        }

        public void Plan(int idsal, DateTime jour, TimeSpan? ha = null, TimeSpan? hd = null)
            => Uow.Inner.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
            {
                Id = Uow.Inner.Get<PlanningSalarie>().Items.Count + 1,
                IdSal = idsal,
                DateP = jour.Date,
                Ha = jour.Date.Add(ha ?? H8),
                Hd = jour.Date.Add(hd ?? H17),
                OffP = false,
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            });

        public void Punch(string matricule, DateTime jour, params TimeSpan[] heures)
        {
            foreach (var heure in heures)
                Uow.Inner.Get<Pointage>().Items.Add(new Pointage
                {
                    MatriculeSage = matricule, DatePointage = jour.Date, HeurePointage = heure,
                    NomBddSage = "SAGE_TEST", NomBddPointeuse = "PTE_TEST"
                });
        }

        public void Conge(int idsal, DateTime jour)
        {
            if (Source.Events.Count == 0)
                Source.Events.Add(new ExternalEvent("CP", "Congé payé"));
            Source.EmployeeEvents.Add(new ExternalEmployeeEvent(idsal, "CP", jour.Date, jour.Date, null, null));
        }

        public void SemaineValidee(string matricule, DateTime lundi)
            => Uow.Inner.Get<HsValidee>().Items.Add(new HsValidee
            {
                BddSage = "SAGE_TEST", Matricules = matricule, DateDeb = lundi, DateFin = lundi.AddDays(6), ValidationHs = true
            });

        public void Ferie(DateTime jour) =>
            Uow.Inner.Get<JourFerie>().Items.Add(new JourFerie { Date = jour, BddSage = "SAGE_TEST" });

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
        result.JoursEmployeControles.Should().Be(10);
        result.Score.Should().Be(76);
        result.NombreAnomalies.Should().Be(4);
        result.Formule.Should().Be(PayrollClosingRules.FormuleScore);
        Compte(result.ParGravite, ClosingSeverity.Critique).Should().Be(2);
        Compte(result.ParGravite, ClosingSeverity.Importante).Should().Be(0);
        Compte(result.ParGravite, ClosingSeverity.Mineure).Should().Be(2);
        Compte(result.ParType, ClosingAnomalyType.PointageIncomplet).Should().Be(1);
        Compte(result.ParType, ClosingAnomalyType.PointageManquant).Should().Be(1);
        Compte(result.ParType, ClosingAnomalyType.DoublonExact).Should().Be(1);
        Compte(result.ParType, ClosingAnomalyType.QuasiDoublon).Should().Be(1);
        Compte(result.ParType, ClosingAnomalyType.PointageSansPlanning).Should().Be(0);
        result.ParType.Count.Should().Be(Enum.GetValues<ClosingAnomalyType>().Length);
    }

    [Fact]
    public async Task Analyse_FixedSet_ScorePerDepartment()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());

        // INFO : 5 + 1 + 1 = 7 sur 5 jours → 72 ; RH : 5 sur 5 jours → 80
        result.ParDepartement.Should().SatisfyRespectively(
            info =>
            {
                info.Departement.Should().Be("INFO");
                info.JoursEmployeControles.Should().Be(5);
                info.Score.Should().Be(72);
                info.NombreAnomalies.Should().Be(3);
            },
            rh =>
            {
                rh.Departement.Should().Be("RH");
                rh.JoursEmployeControles.Should().Be(5);
                rh.Score.Should().Be(80);
                Compte(rh.ParType, ClosingAnomalyType.PointageManquant).Should().Be(1);
            });
    }

    [Fact]
    public async Task Analyse_ExposesMatriculeAndDepartmentOnly()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());
        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain("\"X\"");
        json.Should().NotContain("\"Y\"");
        result.Anomalies.Items.Should().Contain(a => a.Matricule == "2" && a.Departement == "RH");
    }

    [Fact]
    public async Task Analyse_DepartmentFilter_KeepsOnlyThatDepartment()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine(departement: "rh"));

        result.Departement.Should().Be("rh");
        result.JoursEmployeControles.Should().Be(5);
        result.Score.Should().Be(80);
        var anomalie = result.Anomalies.Items.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(nameof(ClosingAnomalyType.PointageManquant));
        result.ParDepartement.Should().ContainSingle().Which.Departement.Should().Be("RH");
    }

    [Fact]
    public async Task Analyse_PeriodWithoutData_ScoreAndComparisonAreNull()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(new DateTime(2025, 10, 1), new DateTime(2025, 10, 5)));

        result.JoursEmployeControles.Should().Be(0);
        result.Score.Should().BeNull();
        result.Comparaison.Should().BeNull();
        result.Anomalies.Items.Should().BeEmpty();
        result.Anomalies.Page.Should().Be(1);
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

        result.JoursEmployeControles.Should().Be(0);
        result.Score.Should().BeNull();
        result.NombreAnomalies.Should().Be(0);
    }

    [Fact]
    public async Task Analyse_PreviousPeriodWithoutData_ComparisonIsNull()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(Semaine());

        result.Score.Should().Be(76);
        result.Comparaison.Should().BeNull();
    }

    [Fact]
    public async Task Analyse_Comparison_Improvement()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var result = await ctx.Service.AnalyserAsync(Semaine() with { IncludeComparison = true });

        // Précédente (12 → 16 nov.) : 2 manquants sur 6 jours → 100 − 100 × 10 / 30 = 66,67 → 67
        var comparaison = result.Comparaison.Should().BeOfType<ClosingComparisonDto>().Which;
        comparaison.From.Should().Be(new DateTime(2025, 11, 12));
        comparaison.To.Should().Be(new DateTime(2025, 11, 16));
        comparaison.JoursEmployeControlesPrecedent.Should().Be(6);
        comparaison.ScorePrecedent.Should().Be(67);
        comparaison.EcartScore.Should().Be(9);
        var manquants = comparaison.ParType.Single(t => t.Type == nameof(ClosingAnomalyType.PointageManquant));
        ((manquants.Precedent, manquants.Actuel, manquants.Ecart)).Should().Be((2, 1, -1));
        var exacts = comparaison.ParType.Single(t => t.Type == nameof(ClosingAnomalyType.DoublonExact));
        ((exacts.Precedent, exacts.Actuel, exacts.Ecart)).Should().Be((0, 1, 1));
    }

    [Fact]
    public async Task Analyse_Comparison_Degradation()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: false);

        var result = await ctx.Service.AnalyserAsync(Semaine() with { IncludeComparison = true });

        var comparaison = result.Comparaison.Should().BeOfType<ClosingComparisonDto>().Which;
        comparaison.ScorePrecedent.Should().Be(100);
        comparaison.EcartScore.Should().Be(-24);
        comparaison.ParType.Should().AllSatisfy(t => t.Ecart.Should().Be(t.Actuel - t.Precedent));
    }

    [Fact]
    public async Task Analyse_Pagination_Page2WithStableOrderAndTotal()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var complet = await ctx.Service.AnalyserAsync(Semaine(page: 1, taille: 100));
        var page2 = await ctx.Service.AnalyserAsync(Semaine(page: 2, taille: 3));

        complet.Anomalies.Items.Select(a => a.Type).Should().Equal(new[]
            {
                nameof(ClosingAnomalyType.PointageManquant), nameof(ClosingAnomalyType.PointageIncomplet),
                nameof(ClosingAnomalyType.DoublonExact), nameof(ClosingAnomalyType.QuasiDoublon)
            });
        page2.Anomalies.Total.Should().Be(4);
        page2.Anomalies.Page.Should().Be(2);
        page2.Anomalies.PageSize.Should().Be(3);
        page2.Anomalies.TotalPages.Should().Be(2);
        page2.Anomalies.Items.Should().ContainSingle().Which.Should().Be(complet.Anomalies.Items[3]);
    }

    [Fact]
    public async Task Analyse_FromAfterTo_ThrowsValidationError()
    {
        var ctx = new Contexte();

        await FluentActions.Awaiting(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Vendredi, Lundi))).Should().ThrowExactlyAsync<DomainException>();
    }

    [Fact]
    public async Task Analyse_PeriodLongerThan366Days_ThrowsValidationError()
    {
        var ctx = new Contexte();
        var from = new DateTime(2024, 1, 1);

        await FluentActions.Awaiting(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(from, from.AddDays(366)))).Should().ThrowExactlyAsync<DomainException>();
        ClosingComputation.Valider(new ClosingAnalysisRequest(from, from.AddDays(365)));
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

        result.NombreAnomalies.Should().Be(0);
        result.JoursEmployeControles.Should().Be(0);
        result.Score.Should().BeNull();
    }

    [Fact]
    public async Task Analyse_SageLeave_ExcludesMissingPunch()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.Conge(2, Lundi);

        var result = await ctx.Service.AnalyserAsync(Semaine());

        Compte(result.ParType, ClosingAnomalyType.PointageManquant).Should().Be(0);
    }

    [Fact]
    public async Task Analyse_ValidatedWeek_FollowsIgnoreOption()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemaineValidee("001", Lundi);

        var parDefaut = await ctx.Service.AnalyserAsync(Semaine());
        var signalees = await ctx.Service.AnalyserAsync(Semaine(options: new ClosingRulesOptions { IgnorerSemainesValidees = false }));

        Compte(parDefaut.ParType, ClosingAnomalyType.PointageIncomplet).Should().Be(0);
        Compte(signalees.ParType, ClosingAnomalyType.PointageIncomplet).Should().Be(1);
    }

    [Fact]
    public async Task Analyse_TwoCalls_IdenticalResult()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var premier = JsonSerializer.Serialize(await ctx.Service.AnalyserAsync(Semaine()));
        var second = JsonSerializer.Serialize(await ctx.Service.AnalyserAsync(Semaine()));

        second.Should().Be(premier);
    }

    [Fact]
    public async Task Analyse_ComparisonNotRequested_IsAbsentByDefault()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var result = await ctx.Service.AnalyserAsync(Semaine());

        Semaine().IncludeComparison.Should().BeFalse();
        result.Comparaison.Should().BeNull();
        result.Score.Should().Be(76);
        result.NombreAnomalies.Should().Be(4);
    }

    [Fact]
    public async Task Analyse_ComparisonNotRequested_PreviousPeriodIsNotLoaded()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var sans = await ctx.Service.ChargerAsync(Semaine(), CancellationToken.None);
        var avec = await ctx.Service.ChargerAsync(Semaine() with { IncludeComparison = true }, CancellationToken.None);

        sans.Pointages.Should().NotContain(p => p.DatePointage < Lundi);
        sans.Plannings.Should().NotContain(p => p.DateP < Lundi.AddDays(-1));
        avec.Pointages.Should().Contain(p => p.DatePointage < Lundi);
    }

    [Fact]
    public async Task Analyse_WithAndWithoutComparison_SameCurrentPeriodResult()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        var sans = await ctx.Service.AnalyserAsync(Semaine());
        var avec = await ctx.Service.AnalyserAsync(Semaine() with { IncludeComparison = true });

        avec.Comparaison.Should().NotBeNull();
        JsonSerializer.Serialize(avec with { Comparaison = null }).Should().Be(JsonSerializer.Serialize(sans));
    }

    [Fact]
    public async Task Analyse_NoPeriod_DefaultsToPreviousCalendarMonth()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();

        var result = await ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(default, default));

        // Aujourd'hui = 1er décembre 2025 : mois civil complet précédent = novembre 2025.
        result.From.Should().Be(new DateTime(2025, 11, 1));
        result.To.Should().Be(new DateTime(2025, 11, 30));
        result.Score.Should().Be(76);
    }

    [Theory]
    [InlineData(2025, 12, 1, 2025, 11, 1, 2025, 11, 30)]
    [InlineData(2025, 12, 31, 2025, 11, 1, 2025, 11, 30)]
    [InlineData(2026, 1, 15, 2025, 12, 1, 2025, 12, 31)]
    [InlineData(2024, 3, 10, 2024, 2, 1, 2024, 2, 29)]
    public void DefaultPeriod_IsPreviousCalendarMonth(int a, int m, int j, int a1, int m1, int j1, int a2, int m2, int j2)
    {
        var request = ClosingComputation.AvecPeriodeParDefaut(new ClosingAnalysisRequest(default, default), new DateTime(a, m, j));

        request.From.Should().Be(new DateTime(a1, m1, j1));
        request.To.Should().Be(new DateTime(a2, m2, j2));
    }

    [Fact]
    public async Task Analyse_OnlyOneBoundProvided_ThrowsValidationError()
    {
        var ctx = new Contexte();

        await FluentActions.Awaiting(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, default))).Should().ThrowExactlyAsync<DomainException>();
        await FluentActions.Awaiting(() =>
            ctx.Service.AnalyserAsync(new ClosingAnalysisRequest(default, Vendredi))).Should().ThrowExactlyAsync<DomainException>();
    }

    [Fact]
    public async Task Analyse_NeverWrites()
    {
        var ctx = new Contexte();
        ctx.JeuFixe();
        ctx.SemainePrecedente(manquantsPour002: true);

        await ctx.Service.AnalyserAsync(Semaine());
        await ctx.Service.AnalyserAsync(Semaine(departement: "INFO", page: 2, taille: 1));

        ctx.Uow.Ecritures.Should().Be(0);
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
                        Json(obtenu).Should().Be(Json(attendu));
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

        avant.NombreAnomalies.Should().Be(4);
        recalcule.NombreAnomalies.Should().Be(3);   // nouvelle analyse : toujours à jour
        reutilise.NombreAnomalies.Should().Be(3);   // l'analyse précédente a remplacé l'entrée du cache
    }

    [Fact]
    public async Task Cache_ReuseServesStoredAnalysis_ThenExpires()
    {
        var horloge = new HorlogeMobile();
        var ctx = new Contexte(horloge);
        ctx.JeuFixe();
        await ctx.Service.AnalyserAsync(Page(1, 50, reuse: false));
        ctx.Punch("002", Lundi, H8, H17);

        (await ctx.Service.AnalyserAsync(Page(2, 1, reuse: true))).NombreAnomalies.Should().Be(4); // dans les 60 s : analyse conservée
        horloge.Avancer(TimeSpan.FromSeconds(61));
        (await ctx.Service.AnalyserAsync(Page(2, 1, reuse: true))).NombreAnomalies.Should().Be(3); // expirée : recalculée
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

        info.Departement.Should().Be("INFO");
        info.NombreAnomalies.Should().Be(3);
        rh.NombreAnomalies.Should().Be(1);
        avecComparaison.Comparaison.Should().NotBeNull();
        autresPoids.Score.Should().NotBe((await ctx.Service.AnalyserAsync(Page(1, 50, reuse: true))).Score);
    }
}
