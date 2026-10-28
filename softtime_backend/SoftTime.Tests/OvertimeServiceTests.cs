using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Tests;

/// <summary>
/// Tests de la chaîne de validation semaine et de la ventilation/exonération, sur le UoW en mémoire
/// (les règles pures sont couvertes par <see cref="OvertimeCalculatorTests"/>).
/// </summary>
public class OvertimeServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Dimanche = Lundi.AddDays(6);

    private static readonly TimeSpan Huit = new(8, 0, 0);
    private static readonly TimeSpan DixSept = new(17, 0, 0);
    private static readonly TimeSpan DixHuit = new(18, 0, 0);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public OvertimeService Service { get; }

        public List<T_HEURECORRIGER> Corrections => Uow.Get<T_HEURECORRIGER>().Items;
        public List<T_HS_VALIDER> Semaines => Uow.Get<T_HS_VALIDER>().Items;
        public List<T_HSSemaine> Repartition => Uow.Get<T_HSSemaine>().Items;
        public List<T_HSExoImp> Exo => Uow.Get<T_HSExoImp>().Items;

        public Contexte(bool multi = true)
        {
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_CLOCK>().Items.Add(new T_CLOCK { MULTIPOINT = multi });
            Uow.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = 1,
                SAGE_MATRICULE = "001",
                SAGE_BDD = "SAGE_TEST",
                POINTEUSE_BDD = "PTE_TEST"
            });
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

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            Service = new OvertimeService(
                Uow,
                tenant,
                Source,
                new PayrollWriterResolver(Array.Empty<IPayrollWriter>()),
                new HistoryArchiveService(Uow, tenant),
                new SageLeaveCalendar(tenant, Source));
        }

        public void Planning(DateTime jour, TimeSpan? pause, TimeSpan? ha = null, TimeSpan? hd = null)
            => Uow.Get<T_PLANNING_SAL>().Items.Add(new T_PLANNING_SAL
            {
                IDSAL = 1,
                DateP = jour.Date,
                HA = jour.Date + (ha ?? Huit),
                HD = jour.Date + (hd ?? DixSept),
                Pause = pause,
                OffP = false,
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            });

        public void Paire(DateTime jour, TimeSpan entree, TimeSpan sortie)
            => Corrections.Add(new T_HEURECORRIGER
            {
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST",
                MATRICULE_SAGE = "001",
                DATE_POINTAGE_IN = jour.Date,
                DATE_POINTAGE_OUT = jour.Date,
                HENTREE = entree,
                HSORTIE = sortie,
                VALIDER_CORRECTION = true,
                ORIGINE = "AUTO"
            });

        public void SemaineValidee(DateTime debut, decimal hs, bool validee = true)
            => Semaines.Add(new T_HS_VALIDER
            {
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTAGE = "PTE_TEST",
                MATRICULES = "001",
                DATE_DEB = debut.Date,
                DATE_FIN = debut.Date.AddDays(6),
                HS = hs,
                VALIDATION_HS = validee
            });

        public Task<IReadOnlyList<WeeklyHsDto>> Apercu(DateTime from, DateTime to)
            => Service.PreviewWeekAsync(new PeriodRequest(null, null, from, to, null));

        public Task<PagedResult<WeeklyHsDto>> ApercuPagine(DateTime from, DateTime to)
            => Service.PreviewWeekPagedAsync(new PeriodRequest(null, null, from, to, null), 1, 50);
    }

    private static void SemaineOuvree(Contexte contexte, int heuresSortie = 17, TimeSpan? pause = null)
    {
        for (var i = 0; i < 5; i++)
        {
            var jour = Lundi.AddDays(i);
            contexte.Planning(jour, pause ?? TimeSpan.Zero);
            contexte.Paire(jour, Huit, heuresSortie == 17 ? DixSept : DixHuit);
        }
    }

    // ------------------------------------------------------------------ calcul hebdomadaire

    [Fact]
    public async Task Apercu_MultiPointage_CalculeLesHSDeLaSemaine()
    {
        var contexte = new Contexte();
        SemaineOuvree(contexte);

        var lignes = await contexte.Apercu(Lundi, Dimanche);

        var ligne = Assert.Single(lignes);
        Assert.Equal(45m, ligne.Ht);
        Assert.Equal(5m, ligne.Hs);
        var persistee = Assert.Single(contexte.Semaines);
        Assert.Equal(45m, persistee.HT);
        Assert.False(persistee.VALIDATION_HS);
    }

    [Fact]
    public async Task Apercu_MonoPointage_UtiliseLesDepassementsJournaliers()
    {
        var contexte = new Contexte(multi: false);
        SemaineOuvree(contexte, heuresSortie: 18, pause: TimeSpan.FromMinutes(30));

        var lignes = await contexte.Apercu(Lundi, Dimanche);

        var ligne = Assert.Single(lignes);
        Assert.Equal(47.5m, ligne.Ht);
        Assert.Equal(5m, ligne.Hs);
    }

    [Fact]
    public async Task Apercu_Conge_CrediteLaJourneeSansAbsence()
    {
        var contexte = new Contexte();
        contexte.Source.Employees.Add(new ExternalEmployee("001", 1, null, null, null, false));
        contexte.Source.Events.Add(new ExternalEvent("0100", "Congé"));
        contexte.Source.EmployeeEvents.Add(new ExternalEmployeeEvent(1, "0100", Lundi, Lundi, false, false));

        contexte.Planning(Lundi, TimeSpan.Zero);
        for (var i = 1; i < 5; i++)
        {
            var jour = Lundi.AddDays(i);
            contexte.Planning(jour, TimeSpan.Zero);
            contexte.Paire(jour, Huit, DixSept);
        }

        var lignes = await contexte.Apercu(Lundi, Dimanche);

        var ligne = Assert.Single(lignes);
        Assert.Equal(44m, ligne.Ht);
        Assert.Equal(4m, ligne.Hs);
        Assert.Equal(0m, ligne.Absence);
    }

    [Fact]
    public async Task Apercu_UneLigneParSalarieMemeSansPointage()
    {
        var contexte = new Contexte();

        var lignes = await contexte.Apercu(Lundi, Dimanche);

        var ligne = Assert.Single(lignes);
        Assert.Equal("001", ligne.Matricule);
        Assert.Equal(0m, ligne.Ht);
        Assert.Equal(0m, ligne.Hs);
    }

    // ------------------------------------------------------------------ validation / annulation

    [Fact]
    public async Task Validation_Puis_Annulation_RouvreLaSemaine()
    {
        var contexte = new Contexte();
        SemaineOuvree(contexte);
        var request = new WeeklyValidationRequest(null, null, Lundi, Dimanche, null, new[] { "001" });

        await contexte.Service.ValidateWeekAsync(request, true);

        var ligne = Assert.Single(contexte.Semaines);
        Assert.True(ligne.VALIDATION_HS);
        Assert.All(contexte.Corrections, h => Assert.True(h.VALIDER_HS));

        // La semaine figée reste visible (pour pouvoir annuler la validation) et n'est pas recalculée.
        var pagine = await contexte.ApercuPagine(Lundi, Dimanche);
        Assert.True(Assert.Single(pagine.Items).Validated);

        await contexte.Service.ValidateWeekAsync(request, false);

        Assert.False(Assert.Single(contexte.Semaines).VALIDATION_HS);
        Assert.All(contexte.Corrections, h => Assert.False(h.VALIDER_HS));
    }

    [Fact]
    public async Task Validation_SansSelectionNeFaitRien()
    {
        var contexte = new Contexte();
        SemaineOuvree(contexte);

        await contexte.Service.ValidateWeekAsync(
            new WeeklyValidationRequest(null, null, Lundi, Dimanche, null, Array.Empty<string>()), true);

        Assert.Empty(contexte.Semaines);
        Assert.All(contexte.Corrections, h => Assert.Null(h.VALIDER_HS));
    }

    // ------------------------------------------------------------------ ventilation / exonération

    [Fact]
    public async Task ExoImpo_NeRegroupeQueLesSemainesValideesEtReconstruitLaRepartition()
    {
        var contexte = new Contexte();
        var debut = Lundi.AddDays(-7);
        var fin = Lundi.AddDays(13);
        contexte.SemaineValidee(debut, 12);
        contexte.SemaineValidee(Lundi, 14);
        contexte.SemaineValidee(Lundi.AddDays(7), 20, validee: false);

        var exo = await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, debut, fin, null));

        var ligne = Assert.Single(exo);
        Assert.Equal(16m, ligne.Exo130);
        Assert.Equal(4m, ligne.Exo150);
        Assert.Equal(0m, ligne.I130);
        Assert.Equal(6m, ligne.I150);

        Assert.Equal(2, contexte.Repartition.Count);
        Assert.Equal(1, contexte.Repartition[0].Semaine);
        Assert.Equal(12m, contexte.Repartition[0].HS);
        Assert.Equal(8m, contexte.Repartition[0].HS1);
        Assert.Equal(4m, contexte.Repartition[0].HS2);
        Assert.Equal(2, contexte.Repartition[1].Semaine);
        Assert.Equal(8m, contexte.Repartition[1].HS1);
        Assert.Equal(6m, contexte.Repartition[1].HS2);
    }

    [Fact]
    public async Task ExoImpo_ReconstruitLaRepartitionAUneReexecution()
    {
        var contexte = new Contexte();
        contexte.SemaineValidee(Lundi, 10);
        var request = new PeriodRequest(null, null, Lundi, Dimanche, null);

        await contexte.Service.CalculateExoImpoAsync(request);
        contexte.Semaines.Single().HS = 6;
        await contexte.Service.CalculateExoImpoAsync(request);

        var semaine = Assert.Single(contexte.Repartition);
        Assert.Equal(6m, semaine.HS);
        Assert.Equal(6m, semaine.HS1);
    }

    [Fact]
    public async Task ExoImpo_ReporteLabsenceDeLaPeriode()
    {
        var contexte = new Contexte();
        contexte.Planning(Lundi, TimeSpan.FromMinutes(30));

        var exo = await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, Lundi, Dimanche, null));

        var ligne = Assert.Single(exo);
        Assert.Equal(8.5m, ligne.Absence);
        Assert.Equal(0m, ligne.Exo130);
        Assert.Empty(contexte.Repartition);
        Assert.Single(contexte.Exo);
    }

    [Fact]
    public async Task ExoImpo_PeriodeDemarrantEnMilieuDeSemaine_InclutLaSemaineEnCours()
    {
        var contexte = new Contexte();
        contexte.SemaineValidee(Lundi, 10);

        // Période 01/09 → 30/09 : la semaine du 31/08 déborde sur le mois et doit être prise en compte.
        var exo = await contexte.Service.CalculateExoImpoAsync(
            new PeriodRequest(null, null, Lundi.AddDays(1), new DateTime(2026, 9, 30), null));

        var ligne = Assert.Single(exo);
        Assert.Equal(8m, ligne.Exo130);
        Assert.Equal(2m, ligne.Exo150);
    }

    // ------------------------------------------------------------------ détail

    [Fact]
    public async Task Detail_ExposeLesBornesReellesDesSemaines()
    {
        // Période 01/05 → 31/05/2025 : la première semaine validée commence avant le mois (28/04).
        var contexte = new Contexte();
        var debutPeriode = new DateTime(2025, 5, 1);
        var finPeriode = new DateTime(2025, 5, 31);
        var semaine1 = new DateTime(2025, 4, 28);
        var semaine2 = new DateTime(2025, 5, 5);
        contexte.SemaineValidee(semaine1, 0m);
        contexte.SemaineValidee(semaine2, 11.21m);
        await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, debutPeriode, finPeriode, null));

        var detail = await contexte.Service.DetailAsync("001", debutPeriode, finPeriode);

        Assert.Equal(2, detail.Semaines.Count);
        Assert.Equal(1, detail.Semaines[0].Semaine!.Value);
        Assert.Equal(semaine1, detail.Semaines[0].Debut!.Value);
        Assert.Equal(semaine1.AddDays(6), detail.Semaines[0].Fin!.Value);
        Assert.Equal(2, detail.Semaines[1].Semaine!.Value);
        Assert.Equal(semaine2, detail.Semaines[1].Debut!.Value);
        Assert.Equal(semaine2.AddDays(6), detail.Semaines[1].Fin!.Value);
        Assert.Equal(11.21m, detail.Semaines[1].Hs);
        Assert.Equal(8m, detail.Semaines[1].Hs1);
        Assert.Equal(3.21m, detail.Semaines[1].Hs2);
    }

    [Fact]
    public async Task Detail_RepliSurLaPeriodeSiLesSemainesNeConcordentPas()
    {
        // Ligne héritée (aucune semaine validée correspondante) : on affiche la période stockée.
        var contexte = new Contexte();
        var debut = new DateTime(2025, 5, 1);
        var fin = new DateTime(2025, 5, 31);
        contexte.Repartition.Add(new T_HSSemaine
        {
            Matricule = "001",
            PeriodeDebut = debut,
            PeriodeFin = fin,
            Semaine = 2,
            HS = 3m,
            HS1 = 3m,
            HS2 = 0m,
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST"
        });

        var detail = await contexte.Service.DetailAsync("001", debut, fin);

        var semaine = Assert.Single(detail.Semaines);
        Assert.Equal(2, semaine.Semaine!.Value);
        Assert.Equal(debut, semaine.Debut!.Value);
        Assert.Equal(fin, semaine.Fin!.Value);
    }
}
