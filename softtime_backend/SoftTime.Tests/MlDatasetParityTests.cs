using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// VÃ©rifie que l'ETL reconstruit les donnÃ©es avec les mÃªmes rÃ¨gles que les Ã©crans : absences
/// (Ã©tat des absences), retards (Ã©tat des retards / tableau de bord) et heures supplÃ©mentaires
/// (Ã©cran HS) doivent se retrouver Ã  l'identique dans la base analytique.
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
        public CardPaie Carte { get; }

        public List<HeureCorrigee> Corrections => Uow.Get<HeureCorrigee>().Items;
        public List<PlanningSalarie> Plannings => Uow.Get<PlanningSalarie>().Items;

        public Contexte()
        {
            Tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);

            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = true });

            Carte = new CardPaie
            {
                Id = 1,
                SageMatricule = "001",
                SageNom = "Dupont",
                SagePrenom = "Jean",
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST",
                Departement = "Atelier",
                Actif = true
            };
            Uow.Get<CardPaie>().Items.Add(Carte);
            Uow.Get<Categorie>().Items.Add(new Categorie
            {
                IdCategorie = 10,
                Intitule = "SÃ©dentaire",
                HeureSemaine = 40m,
                Pause = TimeSpan.FromMinutes(30),
                BddSage = "SAGE_TEST"
            });
            Uow.Get<CategorieSalarie>().Items.Add(new CategorieSalarie
            {
                Id = 1,
                MatriculeSage = 1,
                IdCateg = 10,
                BddSage = "SAGE_TEST"
            });
            Uow.Get<Majoration>().Items.AddRange(new[]
            {
                new Majoration { Mojoration = "NUIT", Cotation = 1m },
                new Majoration { Mojoration = "DIMANCHE", Cotation = 2m },
                new Majoration { Mojoration = "FERIE", Cotation = 3m },
            });

            // Planning : lundi â†’ vendredi, 8 h â€“ 17 h avec 30 min de pause.
            for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
                Plannings.Add(Plan(id: 100 + jour.Day, jour, DixSept, "PTE_TEST"));
        }

        public MlDatasetService Dataset() => MlDatasetTestFactory.Create(
            Uow, Tenant, new SageLeaveCalendar(Tenant, Source), Source, Store,
            Options.Create(new MlOptions { WarmupDays = 0 }), NullLogger<MlDatasetService>.Instance);

        public OvertimeServices Overtime() => new(
            Uow, Tenant, Source,
            new PayrollWriterResolver(Array.Empty<IPayrollWriter>()),
            new HistoryArchiveService(Uow),
            new SageLeaveCalendar(Tenant, Source));
    }

    [Fact]
    public async Task Les_absences_non_justifiees_sont_identiques_a_l_etat_des_absences()
    {
        var ctx = new Contexte();

        // Lundi et mercredi pointÃ©s ; mardi et jeudi absents ; vendredi absence dÃ©clarÃ©e.
        ctx.Corrections.Add(Pointage(Lundi, Huit, DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(2), Huit, DixSept));
        ctx.Corrections.Add(new HeureCorrigee
        {
            BddSage = "SAGE_TEST",
            BddPointeuse = "PTE_TEST",
            MatriculeSage = "001",
            DatePointageIn = Vendredi,
            AbsenceAm = true,
            AbsencePm = true,
            Origine = "MANUEL"
        });

        var result = await ctx.Dataset().BuildAsync(Lundi, Dimanche, persist: true);

        var planned = ctx.Store.Daily.Where(d => d.EstJourPlanifie).ToList();
        Assert.Equal(5, planned.Count);
        Assert.Equal(2, ctx.Store.Daily.Count(d => d.AbsenceNonJustifiee));
        Assert.True(ctx.Store.Daily.Single(d => d.Date == Vendredi).AbsenceDeclaree);
        Assert.False(ctx.Store.Daily.Single(d => d.Date == Vendredi).AbsenceNonJustifiee);

        // ParitÃ© avec l'Ã©tat des absences, sur la mÃªme population et la mÃªme rÃ¨gle.
        var absences = AbsenceReportRules.Calculer(
            new EmployeeSources(new[] { ctx.Carte }, ctx.Plannings, ctx.Corrections),
            new AbsenceCalendar(new Dictionary<string, List<SoftTime.Domain.Models.LeaveDay>>(), new HashSet<DateTime>()),
            new ReportPeriod(Lundi, Dimanche, Dimanche.AddDays(1)));

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
        Assert.False(ctx.Store.Weekly[0].HsValidee);
        Assert.Equal(1, result.WeeklyRows);
    }

    [Fact]
    public async Task Les_retards_sont_identiques_a_l_etat_des_retards_sans_valeur_saisie()
    {
        var ctx = new Contexte();
        ctx.Uow.Get<ToleranceRetard>().Items.Add(new ToleranceRetard { Id = 1, IdCategorie = 10, Tolerance = 5, TypesTol = false });

        // Aucune valeur RETARD saisie : le retard vient de la premiÃ¨re entrÃ©e vs 8 h + 5 min.
        ctx.Corrections.Add(Pointage(Lundi, new TimeSpan(8, 20, 0), DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(1), new TimeSpan(7, 55, 0), DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(2), new TimeSpan(8, 4, 0), DixSept));
        ctx.Corrections.Add(Pointage(Lundi.AddDays(3), new TimeSpan(9, 0, 0), DixSept));

        await ctx.Dataset().BuildAsync(Lundi, Dimanche, persist: true);

        var etat = RetardReportRules.Calculer(
                new EmployeeSources(new[] { ctx.Carte }, ctx.Plannings, ctx.Corrections),
                new RetardTolerances(new Dictionary<int, int> { [1] = 10 }, ctx.Uow.Get<ToleranceRetard>().Items),
                new HashSet<DateTime>(),
                new ReportPeriod(Lundi, Dimanche, Dimanche.AddDays(1)))
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

        // Planning d'une autre pointeuse, d'ID plus petit : ignorÃ© par l'Ã©cran HS.
        ctx.Plannings.Add(Plan(id: 1, Lundi, new TimeSpan(20, 0, 0), "AUTRE_PTE"));

        // 8 h â€“ 19 h chaque jour, et une ligne d'absence dÃ©clarÃ©e seule le samedi.
        for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
            ctx.Corrections.Add(Pointage(jour, Huit, new TimeSpan(19, 0, 0)));
        ctx.Corrections.Add(new HeureCorrigee
        {
            BddSage = "SAGE_TEST",
            BddPointeuse = "PTE_TEST",
            MatriculeSage = "001",
            DatePointageIn = Dimanche.AddDays(-1),
            AbsenceAm = true,
            Origine = "MANUEL"
        });

        await ctx.Dataset().BuildAsync(Lundi, Dimanche, persist: true);
        var ecran = (await ctx.Overtime().PreviewWeekPagedAsync(new PeriodRequest(null, null, Lundi, Dimanche), 1, 50))
            .Items.Single();

        var semaine = Assert.Single(ctx.Store.Weekly);
        Assert.True(ecran.Hs > 0m);
        Assert.Equal(Math.Round(ecran.Hs ?? 0m, 2), semaine.Hs);
        Assert.Equal(Math.Round(ecran.Absence ?? 0m, 2), semaine.AbsenceHeures);
    }

    [Fact]
    public async Task Les_jours_futurs_ne_sont_ni_des_absences_ni_de_l_historique()
    {
        var ctx = new Contexte();
        var aujourdHui = DateTime.Today;

        // Hier pointÃ©, puis 7 jours planifiÃ©s Ã  venir (donc sans pointage par construction).
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
            .Concat(Enumerable.Range(0, 3).Select(i => (petit, true, true)));         // trop peu planifiÃ©s

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

    private static PlanningSalarie Plan(int id, DateTime jour, TimeSpan fin, string pointeuse) => new()
    {
        Id = id,
        IdSal = 1,
        DateP = jour,
        Ha = jour + Huit,
        Hd = jour + fin,
        Pause = TimeSpan.FromMinutes(30),
        OffP = false,
        BddSage = "SAGE_TEST",
        BddPointeuse = pointeuse
    };

    private static HeureCorrigee Pointage(DateTime jour, TimeSpan entree, TimeSpan sortie) => new()
    {
        BddSage = "SAGE_TEST",
        BddPointeuse = "PTE_TEST",
        MatriculeSage = "001",
        DatePointageIn = jour,
        DatePointageOut = jour,
        HEntree = entree,
        HSortie = sortie,
        Origine = "AUTO"
    };
}
