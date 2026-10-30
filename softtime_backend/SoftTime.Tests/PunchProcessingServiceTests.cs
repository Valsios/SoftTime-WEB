using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class PunchProcessingServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Samedi = new(2026, 9, 5);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public PunchProcessingService Service { get; }
        public List<T_HEURECORRIGER> Corrections => Uow.Get<T_HEURECORRIGER>().Items;
        public List<T_HEUREANOMALIE> Anomalies => Uow.Get<T_HEUREANOMALIE>().Items;

        public Contexte(bool multi = false)
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
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            Service = new PunchProcessingService(Uow, tenant);
        }

        public void Pointage(DateTime jour, int heure, int minute = 0)
        {
            Uow.Get<T_POINTAGE>().Items.Add(new T_POINTAGE
            {
                NOM_BDD_SAGE = "SAGE_TEST",
                NOM_BDD_POINTEUSE = "PTE_TEST",
                MATRICULE_SAGE = "001",
                DATE_POINTAGE = jour.Date,
                HEURE_POINTAGE = new TimeSpan(heure, minute, 0),
                DATE_IMPORTATION = DateTime.Now
            });
        }

        public void Ferie(DateTime jour)
        {
            Uow.Get<T_FERIE>().Items.Add(new T_FERIE
            {
                DATE = jour.Date,
                INTITULE = "Férié test",
                BDD_SAGE = "SAGE_TEST"
            });
        }

        public T_PLANNING_SAL Planning(DateTime jour, DateTime ha, DateTime hd, bool off = false)
        {
            var planning = new T_PLANNING_SAL
            {
                IDSAL = 1,
                DateP = jour.Date,
                HA = ha,
                HD = hd,
                OffP = off,
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            };
            Uow.Get<T_PLANNING_SAL>().Items.Add(planning);
            return planning;
        }

        public Task<PreprocessPunchesResultDto> Traiter(DateTime from, DateTime to)
            => Service.ProcessAsync(new PeriodRequest(null, null, from, to));
    }

    [Fact]
    public async Task Mono_JourOuvre_SansPointage_CreeUneAnomalieAbsence()
    {
        var contexte = new Contexte();

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(0, resultat.CorrectionsCreees);
        var anomalie = Assert.Single(contexte.Anomalies);
        Assert.True(anomalie.ABS_AM);
        Assert.True(anomalie.ABS_PM);
        Assert.Null(anomalie.H1);
        Assert.Null(anomalie.H2);
    }

    [Fact]
    public async Task Mono_UnPointage_CreeUneAnomalieAvecH1EtH2()
    {
        var contexte = new Contexte();
        contexte.Pointage(Lundi, 8);

        await contexte.Traiter(Lundi, Lundi);

        var anomalie = Assert.Single(contexte.Anomalies);
        Assert.Equal(new TimeSpan(8, 0, 0), anomalie.H1);
        Assert.Equal(new TimeSpan(8, 0, 0), anomalie.H2);
        Assert.Null(anomalie.ABS_AM);
        Assert.Null(anomalie.ABS_PM);
    }

    [Fact]
    public async Task Mono_DeuxPointages_CreeUneCorrectionPremierDernier()
    {
        var contexte = new Contexte();
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        var correction = Assert.Single(contexte.Corrections);
        Assert.Equal(new TimeSpan(8, 0, 0), correction.HENTREE);
        Assert.Equal(new TimeSpan(17, 0, 0), correction.HSORTIE);
        Assert.Equal(Lundi.Date, correction.DATE_POINTAGE_IN);
        Assert.Equal(Lundi.Date, correction.DATE_POINTAGE_OUT);
        Assert.True(correction.VALIDER_CORRECTION);
        Assert.Equal(PunchProcessingConfiguration.OrigineAuto, correction.ORIGINE);
        Assert.Null(correction.FERIER);
        Assert.Empty(contexte.Anomalies);
        Assert.Equal(1, resultat.CorrectionsCreees);
    }

    [Fact]
    public async Task Mono_Ferie_DeuxPointages_DrapeauxFeries()
    {
        var contexte = new Contexte();
        contexte.Ferie(Lundi);
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        await contexte.Traiter(Lundi, Lundi);

        var correction = Assert.Single(contexte.Corrections);
        Assert.True(correction.FERIER);
        Assert.True(correction.FERIER_AM);
        Assert.True(correction.FERIER_PM);
    }

    [Fact]
    public async Task Mono_Ferie_SansPointage_NeCreeRien()
    {
        var contexte = new Contexte();
        contexte.Ferie(Lundi);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Empty(contexte.Anomalies);
        Assert.Equal(0, resultat.AnomaliesCreees);
    }

    [Fact]
    public async Task Mono_Samedi_DeuxPointages_CreeUneCorrectionSansFerie()
    {
        var contexte = new Contexte();
        contexte.Pointage(Samedi, 8);
        contexte.Pointage(Samedi, 17);

        var resultat = await contexte.Traiter(Samedi, Samedi);

        var correction = Assert.Single(contexte.Corrections);
        Assert.Null(correction.FERIER);
        Assert.Equal(1, resultat.CorrectionsCreees);
    }

    [Fact]
    public async Task Mono_Samedi_SansPointage_NeCreeRien()
    {
        var contexte = new Contexte();

        await contexte.Traiter(Samedi, Samedi);

        Assert.Empty(contexte.Anomalies);
    }

    [Fact]
    public async Task Mono_IgnoreLePlanningDeNuit_TraiteChaqueJourSeparement()
    {
        var mardi = Lundi.AddDays(1);
        var contexte = new Contexte();
        contexte.Planning(Lundi, Lundi.AddHours(22), mardi.AddHours(6));
        contexte.Pointage(Lundi, 22);
        contexte.Pointage(mardi, 6);

        await contexte.Traiter(Lundi, mardi);

        Assert.Equal(2, contexte.Anomalies.Count);
        Assert.Empty(contexte.Corrections);
    }

    [Fact]
    public async Task Multi_QuatrePointages_DeuxCorrections()
    {
        var contexte = new Contexte(multi: true);
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 12);
        contexte.Pointage(Lundi, 13);
        contexte.Pointage(Lundi, 17);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(2, contexte.Corrections.Count);
        Assert.Equal(2, resultat.CorrectionsCreees);
        Assert.Equal(new TimeSpan(8, 0, 0), contexte.Corrections[0].HENTREE);
        Assert.Equal(new TimeSpan(12, 0, 0), contexte.Corrections[0].HSORTIE);
        Assert.Equal(new TimeSpan(13, 0, 0), contexte.Corrections[1].HENTREE);
        Assert.Equal(new TimeSpan(17, 0, 0), contexte.Corrections[1].HSORTIE);
        Assert.Empty(contexte.Anomalies);
    }

    [Fact]
    public async Task Multi_NombreImpair_CreeUneAnomalieH1H2()
    {
        var contexte = new Contexte(multi: true);
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 13);
        contexte.Pointage(Lundi, 17);

        await contexte.Traiter(Lundi, Lundi);

        var anomalie = Assert.Single(contexte.Anomalies);
        Assert.Equal(new TimeSpan(8, 0, 0), anomalie.H1);
        Assert.Equal(new TimeSpan(17, 0, 0), anomalie.H2);
        Assert.Empty(contexte.Corrections);
    }

    [Fact]
    public async Task Multi_JourSansPointage_AvecPlanningTravaille_CreeUneAnomalie()
    {
        var contexte = new Contexte(multi: true);
        contexte.Planning(Lundi, Lundi.AddHours(8), Lundi.AddHours(17));

        await contexte.Traiter(Lundi, Lundi);

        var anomalie = Assert.Single(contexte.Anomalies);
        Assert.True(anomalie.ABS_AM);
        Assert.True(anomalie.ABS_PM);
    }

    [Fact]
    public async Task Multi_JourSansPointage_SansPlanning_NeCreeRien()
    {
        var contexte = new Contexte(multi: true);

        await contexte.Traiter(Lundi, Lundi);

        Assert.Empty(contexte.Anomalies);
    }

    [Fact]
    public async Task Multi_OffP_SansPointage_NeCreeRien()
    {
        var contexte = new Contexte(multi: true);
        contexte.Planning(Lundi, Lundi.AddHours(8), Lundi.AddHours(17), off: true);

        await contexte.Traiter(Lundi, Lundi);

        Assert.Empty(contexte.Anomalies);
    }

    [Fact]
    public async Task Multi_Nuit_AvecPlanning_CreeUnePaireSurDeuxJours()
    {
        var mardi = Lundi.AddDays(1);
        var contexte = new Contexte(multi: true);
        contexte.Planning(Lundi, Lundi.AddHours(22), mardi.AddHours(6));
        contexte.Pointage(Lundi, 22);
        contexte.Pointage(mardi, 6);

        await contexte.Traiter(Lundi, mardi);

        var correction = Assert.Single(contexte.Corrections);
        Assert.Equal(Lundi.Date, correction.DATE_POINTAGE_IN);
        Assert.Equal(mardi.Date, correction.DATE_POINTAGE_OUT);
        Assert.Equal(new TimeSpan(22, 0, 0), correction.HENTREE);
        Assert.Equal(new TimeSpan(6, 0, 0), correction.HSORTIE);
        Assert.Empty(contexte.Anomalies);
    }

    [Fact]
    public async Task CorrectionManuelle_EstProtegee()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<T_HEURECORRIGER>().Items.Add(new T_HEURECORRIGER
        {
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE_IN = Lundi.Date,
            DATE_POINTAGE_OUT = Lundi.Date,
            HENTREE = new TimeSpan(8, 0, 0),
            HSORTIE = new TimeSpan(16, 0, 0),
            ORIGINE = PunchProcessingConfiguration.OrigineManuel
        });
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(1, resultat.JoursIgnores);
        Assert.Single(contexte.Corrections);
        Assert.Equal(PunchProcessingConfiguration.OrigineManuel, contexte.Corrections[0].ORIGINE);
        Assert.Equal(new TimeSpan(16, 0, 0), contexte.Corrections[0].HSORTIE);
    }

    [Fact]
    public async Task SemaineValidee_EstProtegee()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<T_HS_VALIDER>().Items.Add(new T_HS_VALIDER
        {
            BDD_SAGE = "SAGE_TEST",
            MATRICULES = "001",
            DATE_DEB = Lundi.Date,
            VALIDATION_HS = true
        });
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(1, resultat.JoursIgnores);
        Assert.Empty(contexte.Corrections);
    }

    [Fact]
    public async Task Reexecution_Corrections_EstIdempotente()
    {
        var contexte = new Contexte();
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        var premier = await contexte.Traiter(Lundi, Lundi);
        var second = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(1, premier.CorrectionsCreees);
        Assert.Equal(1, second.CorrectionsCreees);
        Assert.Single(contexte.Corrections);
    }

    [Fact]
    public async Task Reexecution_Anomalies_MetAJourSansDupliquer()
    {
        var contexte = new Contexte();
        contexte.Pointage(Lundi, 8);

        var premier = await contexte.Traiter(Lundi, Lundi);
        var second = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(1, premier.AnomaliesCreees);
        Assert.Equal(0, second.AnomaliesCreees);
        Assert.Single(contexte.Anomalies);
    }

    [Fact]
    public async Task Mono_Ferie_SansPointage_AvecPlanning_NeCreePasDAnomalie()
    {
        var contexte = new Contexte();
        contexte.Ferie(Lundi);
        contexte.Planning(Lundi, Lundi.AddHours(8), Lundi.AddHours(17));

        await contexte.Traiter(Lundi, Lundi);

        Assert.Empty(contexte.Anomalies);
    }
}
