using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Vérifie que l'ETL reconstruit les données avec les mêmes règles que les écrans : absences
/// (état des absences), retards (état des retards / tableau de bord) et heures supplémentaires
/// (écran HS) doivent se retrouver à l'identique dans la base analytique.
/// </summary>
public class MlDatasetParityTests
{
    private static readonly DateTime Lundi = new(2026, 9, 7);
    private static readonly DateTime Vendredi = Lundi.AddDays(4);
    private static readonly DateTime Dimanche = Lundi.AddDays(6);
    private static readonly TimeSpan Huit = new(8, 0, 0);
    private static readonly TimeSpan DixSept = new(17, 0, 0);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public FakeMlStore Store { get; } = new();
        public TenantConnectionService Tenant { get; }
        public T_CARDPAIE Carte { get; }

        public List<T_HEURECORRIGER> Corrections => Uow.Get<T_HEURECORRIGER>().Items;
        public List<T_PLANNING_SAL> Plannings => Uow.Get<T_PLANNING_SAL>().Items;

        public Contexte()
        {
            Tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);

            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1, MULTIPOINT = true });

            Carte = new T_CARDPAIE
            {
                ID = 1,
                SAGE_MATRICULE = "001",
                SAGE_NOM = "Dupont",
                SAGE_PRENOM = "Jean",
                SAGE_BDD = "SAGE_TEST",
                POINTEUSE_BDD = "PTE_TEST",
                DEPARTEMENT = "Atelier",
                ACTIF = true
            };
            Uow.Get<T_CARDPAIE>().Items.Add(Carte);
            Uow.Get<T_CATEGORIE>().Items.Add(new T_CATEGORIE
            {
                IDCATEGORIE = 10,
                INTITULE = "Sédentaire",
                HEURESEMAINE = 40m,
                PAUSE = TimeSpan.FromMinutes(30),
                BDD_SAGE = "SAGE_TEST"
            });
            Uow.Get<T_CAT_SAL>().Items.Add(new T_CAT_SAL
            {
                ID = 1,
                MATRICULE_SAGE = 1,
                ID_CATEG = 10,
                BDD_SAGE = "SAGE_TEST"
            });
            Uow.Get<T_MAJORATION>().Items.AddRange(new[]
            {
                new T_MAJORATION { Mojoration = "NUIT", Cotation = 1m },
                new T_MAJORATION { Mojoration = "DIMANCHE", Cotation = 2m },
                new T_MAJORATION { Mojoration = "FERIE", Cotation = 3m },
            });

            // Planning : lundi → vendredi, 8 h – 17 h avec 30 min de pause.
            for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
                Plannings.Add(Plan(id: 100 + jour.Day, jour, DixSept, "PTE_TEST"));
        }

        public MlDatasetService Dataset() => new(
            Uow, Tenant, new SageLeaveCalendar(Tenant, Source), Source, Store,
            Options.Create(new MlOptions { WarmupDays = 0 }), NullLogger<MlDatasetService>.Instance);

        public OvertimeService Overtime() => new(
            Uow, Tenant, Source,
            new PayrollWriterResolver(Array.Empty<IPayrollWriter>()),
            new HistoryArchiveService(Uow, Tenant),
            new SageLeaveCalendar(Tenant, Source));
    }

    [Fact]
    public async Task Les_absences_non_justifiees_sont_identiques_a_l_etat_des_absences()
    {
        var ctx = new Contexte();

        // Lundi et mercredi pointés ; mardi et jeudi absents ; vendredi absence déclarée.
        ctx.Corrections.Add(Pointage(Lundi, Huit, DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(2), Huit, DixSept));
        ctx.Corrections.Add(new T_HEURECORRIGER
        {
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE_IN = Vendredi,
            ABSENCE_AM = true,
            ABSENCE_PM = true,
            ORIGINE = "MANUEL"
        });

        var result = await ctx.Dataset().BuildAsync(Lundi, Dimanche, persist: true);

        var planned = ctx.Store.Daily.Where(d => d.EstJourPlanifie).ToList();
        Assert.Equal(5, planned.Count);
        Assert.Equal(2, ctx.Store.Daily.Count(d => d.AbsenceNonJustifiee));
        Assert.True(ctx.Store.Daily.Single(d => d.Date == Vendredi).AbsenceDeclaree);
        Assert.False(ctx.Store.Daily.Single(d => d.Date == Vendredi).AbsenceNonJustifiee);

        // Parité avec l'état des absences, sur la même population et la même règle.
        var absences = AbsenceReportRules.Calculer(
            ctx.Plannings,
            new[] { ctx.Carte },
            ctx.Corrections,
            new Dictionary<string, List<SoftTime.Domain.Models.LeaveDay>>(),
            new HashSet<DateTime>(),
            Lundi,
            Dimanche,
            Dimanche.AddDays(1));

        var datesEtat = absences
            .Where(a => a.Type == AbsenceReportRules.TypeNonJustifiee)
            .Select(a => a.Date.Date)
            .OrderBy(d => d)
            .ToList();
        var datesMl = ctx.Store.Daily
            .Where(d => d.AbsenceNonJustifiee)
            .Select(d => d.Date.Date)
            .OrderBy(d => d)
            .ToList();
        Assert.Equal(datesEtat, datesMl);

        Assert.Single(ctx.Store.Weekly);
        Assert.False(ctx.Store.Weekly[0].HSValidee);
        Assert.Equal(1, result.WeeklyRows);
    }

    [Fact]
    public async Task Les_retards_sont_identiques_a_l_etat_des_retards_sans_valeur_saisie()
    {
        var ctx = new Contexte();
        ctx.Uow.Get<T_TOLERANCE>().Items.Add(new T_TOLERANCE { ID = 1, IDCATEGORIE = 10, TOLERANCE = 5, TYPES_TOL = false });

        // Aucune valeur RETARD saisie : le retard vient de la première entrée vs 8 h + 5 min.
        ctx.Corrections.Add(Pointage(Lundi, new TimeSpan(8, 20, 0), DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(1), new TimeSpan(7, 55, 0), DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(2), new TimeSpan(8, 4, 0), DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(3), new TimeSpan(9, 0, 0), DixSept));

        await ctx.Dataset().BuildAsync(Lundi, Dimanche, persist: true);

        var etat = RetardReportRules.Calculer(
                new[] { ctx.Carte },
                ctx.Corrections,
                ctx.Plannings,
                new Dictionary<int, int> { [1] = 10 },
                ctx.Uow.Get<T_TOLERANCE>().Items,
                new HashSet<DateTime>(),
                Lundi,
                Dimanche,
                Dimanche.AddDays(1))
            .Where(r => r.MinutesRetard > 0)
            .ToDictionary(r => r.Date.Date, r => r.MinutesRetard);
        var ml = ctx.Store.Daily
            .Where(d => (d.RetardMinutes ?? 0) > 0)
            .ToDictionary(d => d.Date.Date, d => d.RetardMinutes ?? 0);

        Assert.Equal(2, etat.Count);
        Assert.Equal(etat.OrderBy(kv => kv.Key), ml.OrderBy(kv => kv.Key));
        Assert.Equal(15, ml[Lundi]);
        Assert.Equal(55, ml[Lundi.AddDays(3)]);
    }

    [Fact]
    public async Task Les_heures_sup_sont_identiques_a_l_ecran_HS()
    {
        var ctx = new Contexte();

        // Planning d'une autre pointeuse, d'ID plus petit : ignoré par l'écran HS.
        ctx.Plannings.Add(Plan(id: 1, Lundi, new TimeSpan(20, 0, 0), "AUTRE_PTE"));

        // 8 h – 19 h chaque jour, et une ligne d'absence déclarée seule le samedi.
        for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
            ctx.Corrections.Add(Pointage(jour, Huit, new TimeSpan(19, 0, 0)));
        ctx.Corrections.Add(new T_HEURECORRIGER
        {
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE_IN = Dimanche.AddDays(-1),
            ABSENCE_AM = true,
            ORIGINE = "MANUEL"
        });

        await ctx.Dataset().BuildAsync(Lundi, Dimanche, persist: true);
        var ecran = (await ctx.Overtime().PreviewWeekPagedAsync(new PeriodRequest(null, null, Lundi, Dimanche), 1, 50))
            .Items.Single();

        var semaine = Assert.Single(ctx.Store.Weekly);
        Assert.True(ecran.Hs > 0m);
        Assert.Equal(Math.Round(ecran.Hs ?? 0m, 2), semaine.HS);
        Assert.Equal(Math.Round(ecran.Absence ?? 0m, 2), semaine.AbsenceHeures);
    }

    [Fact]
    public async Task Les_jours_futurs_ne_sont_ni_des_absences_ni_de_l_historique()
    {
        var ctx = new Contexte();
        var aujourdHui = DateTime.Today;

        // Hier pointé, puis 7 jours planifiés à venir (donc sans pointage par construction).
        ctx.Plannings.Add(Plan(id: 500, aujourdHui.AddDays(-1), DixSept, "PTE_TEST"));
        ctx.Corrections.Add(Pointage(aujourdHui.AddDays(-1), Huit, DixSept));
        for (var i = 0; i < 7; i++)
            ctx.Plannings.Add(Plan(id: 501 + i, aujourdHui.AddDays(i), DixSept, "PTE_TEST"));

        var lignes = await ctx.Dataset().BuildInferenceDailyAsync(aujourdHui, aujourdHui.AddDays(6));

        Assert.NotEmpty(lignes);
        Assert.All(lignes, l =>
        {
            Assert.False(l.AbsenceNonJustifiee);
            Assert.Equal(0, l.Absences7j);
            Assert.Equal(0d, l.TauxAbsence30j);
        });
    }

    [Fact]
    public void Un_jour_ou_la_majorite_n_a_pas_pointe_est_un_trou_de_donnees()
    {
        var options = new MlOptions { MissingPunchDayAbsenceRate = 0.5, MissingPunchDayMinPlanned = 5 };
        var trou = new DateTime(2025, 6, 2);
        var normal = trou.AddDays(1);
        var petit = trou.AddDays(2);

        var jours = Enumerable.Range(0, 10).Select(i => (trou, true, i < 8))         // 80 % : trou
            .Concat(Enumerable.Range(0, 10).Select(i => (normal, true, i < 1)))       // 10 % : normal
            .Concat(Enumerable.Range(0, 3).Select(i => (petit, true, true)));         // trop peu planifiés

        var resultat = MlDataQuality.JoursSansPointage(jours, options);

        Assert.Equal(new[] { trou }, resultat.ToArray());
    }

    [Fact]
    public void Les_tranches_du_backfill_couvrent_des_semaines_completes()
    {
        var from = new DateTime(2025, 7, 30);   // mercredi
        var to = new DateTime(2025, 12, 17);    // mercredi

        var tranches = MlPipelineService.Chunks(from, to, 3).ToList();

        Assert.Equal(DayOfWeek.Monday, tranches[0].Start.DayOfWeek);
        Assert.True(tranches[0].Start <= from);
        Assert.Equal(to, tranches[^1].End);
        foreach (var (start, end) in tranches.SkipLast(1))
        {
            Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
            Assert.Equal(DayOfWeek.Sunday, end.DayOfWeek);
        }
        for (var i = 1; i < tranches.Count; i++)
            Assert.Equal(tranches[i - 1].End.AddDays(1), tranches[i].Start);
    }

    private static T_PLANNING_SAL Plan(int id, DateTime jour, TimeSpan fin, string pointeuse) => new()
    {
        ID = id,
        IDSAL = 1,
        DateP = jour,
        HA = jour + Huit,
        HD = jour + fin,
        Pause = TimeSpan.FromMinutes(30),
        OffP = false,
        BDD_SAGE = "SAGE_TEST",
        BDD_POINTEUSE = pointeuse
    };

    private static T_HEURECORRIGER Pointage(DateTime jour, TimeSpan entree, TimeSpan sortie) => new()
    {
        BDD_SAGE = "SAGE_TEST",
        BDD_POINTEUSE = "PTE_TEST",
        MATRICULE_SAGE = "001",
        DATE_POINTAGE_IN = jour,
        DATE_POINTAGE_OUT = jour,
        HENTREE = entree,
        HSORTIE = sortie,
        ORIGINE = "AUTO"
    };
}
