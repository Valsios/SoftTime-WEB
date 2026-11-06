using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

public class PunchProcessingServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Samedi = new(2026, 9, 5);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public PunchProcessingService Service { get; }
        public List<HeureCorrigee> Corrections => Uow.Get<HeureCorrigee>().Items;
        public List<HeureAnomalie> Anomalies => Uow.Get<HeureAnomalie>().Items;

        public Contexte(bool multi = false)
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { MultiPoint = multi });
            Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = 1,
                SageMatricule = "001",
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST"
            });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            Service = new PunchProcessingService(Uow, tenant);
        }

        public void Pointage(DateTime jour, int heure, int minute = 0)
        {
            Uow.Get<Pointage>().Items.Add(new Pointage
            {
                NomBddSage = "SAGE_TEST",
                NomBddPointeuse = "PTE_TEST",
                MatriculeSage = "001",
                DatePointage = jour.Date,
                HeurePointage = new TimeSpan(heure, minute, 0),
                DateImportation = DateTime.Now
            });
        }

        public void Ferie(DateTime jour)
        {
            Uow.Get<JourFerie>().Items.Add(new JourFerie
            {
                Date = jour.Date,
                Intitule = "Férié test",
                BddSage = "SAGE_TEST"
            });
        }

        public PlanningSalarie Planning(DateTime jour, DateTime ha, DateTime hd, bool off = false)
        {
            var planning = new PlanningSalarie
            {
                IdSal = 1,
                DateP = jour.Date,
                Ha = ha,
                Hd = hd,
                OffP = off,
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            };
            Uow.Get<PlanningSalarie>().Items.Add(planning);
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
        Assert.True(anomalie.AbsAm);
        Assert.True(anomalie.AbsPm);
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
        Assert.Null(anomalie.AbsAm);
        Assert.Null(anomalie.AbsPm);
    }

    [Fact]
    public async Task Mono_DeuxPointages_CreeUneCorrectionPremierDernier()
    {
        var contexte = new Contexte();
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        var correction = Assert.Single(contexte.Corrections);
        Assert.Equal(new TimeSpan(8, 0, 0), correction.HEntree);
        Assert.Equal(new TimeSpan(17, 0, 0), correction.HSortie);
        Assert.Equal(Lundi.Date, correction.DatePointageIn);
        Assert.Equal(Lundi.Date, correction.DatePointageOut);
        Assert.True(correction.ValiderCorrection);
        Assert.Equal(PunchProcessingConfiguration.OrigineAuto, correction.Origine);
        Assert.Null(correction.Ferier);
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
        Assert.True(correction.Ferier);
        Assert.True(correction.FerierAm);
        Assert.True(correction.FerierPm);
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
        Assert.Null(correction.Ferier);
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
        Assert.Equal(new TimeSpan(8, 0, 0), contexte.Corrections[0].HEntree);
        Assert.Equal(new TimeSpan(12, 0, 0), contexte.Corrections[0].HSortie);
        Assert.Equal(new TimeSpan(13, 0, 0), contexte.Corrections[1].HEntree);
        Assert.Equal(new TimeSpan(17, 0, 0), contexte.Corrections[1].HSortie);
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
        Assert.True(anomalie.AbsAm);
        Assert.True(anomalie.AbsPm);
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
        Assert.Equal(Lundi.Date, correction.DatePointageIn);
        Assert.Equal(mardi.Date, correction.DatePointageOut);
        Assert.Equal(new TimeSpan(22, 0, 0), correction.HEntree);
        Assert.Equal(new TimeSpan(6, 0, 0), correction.HSortie);
        Assert.Empty(contexte.Anomalies);
    }

    [Fact]
    public async Task CorrectionManuelle_EstProtegee()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<HeureCorrigee>().Items.Add(new HeureCorrigee
        {
            BddSage = "SAGE_TEST",
            BddPointeuse = "PTE_TEST",
            MatriculeSage = "001",
            DatePointageIn = Lundi.Date,
            DatePointageOut = Lundi.Date,
            HEntree = new TimeSpan(8, 0, 0),
            HSortie = new TimeSpan(16, 0, 0),
            Origine = PunchProcessingConfiguration.OrigineManuel
        });
        contexte.Pointage(Lundi, 8);
        contexte.Pointage(Lundi, 17);

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Equal(1, resultat.JoursIgnores);
        Assert.Single(contexte.Corrections);
        Assert.Equal(PunchProcessingConfiguration.OrigineManuel, contexte.Corrections[0].Origine);
        Assert.Equal(new TimeSpan(16, 0, 0), contexte.Corrections[0].HSortie);
    }

    [Fact]
    public async Task SemaineValidee_EstProtegee()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<HsValidee>().Items.Add(new HsValidee
        {
            BddSage = "SAGE_TEST",
            Matricules = "001",
            DateDeb = Lundi.Date,
            ValidationHs = true
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

    private static HeureCorrigee CorrectionAuto(string matricule, DateTime jour) => new()
    {
        BddSage = "SAGE_TEST",
        BddPointeuse = "PTE_TEST",
        MatriculeSage = matricule,
        DatePointageIn = jour.Date,
        DatePointageOut = jour.Date,
        HEntree = new TimeSpan(8, 0, 0),
        HSortie = new TimeSpan(17, 0, 0),
        Origine = "AUTO"
    };

    [Fact]
    public async Task Traitement_FiltreDepartement_NePurgePasLesCorrectionsDesAutresSalaries()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<CardPaie>().Items[0].Departement = "ATELIER";
        contexte.Uow.Get<CardPaie>().Items.Add(new CardPaie
        {
            Id = 2,
            SageMatricule = "002",
            SageBdd = "SAGE_TEST",
            PointeuseBdd = "PTE_TEST",
            Departement = "BUREAU"
        });
        var autre = CorrectionAuto("002", Lundi);
        contexte.Corrections.Add(autre);

        await contexte.Service.ProcessAsync(new PeriodRequest(null, null, Lundi, Lundi, "ATELIER"));

        Assert.Contains(autre, contexte.Corrections);
    }

    [Fact]
    public async Task Traitement_SemaineValidee_ConserveSesCorrectionsAuto()
    {
        var contexte = new Contexte();
        var validee = CorrectionAuto("001", Lundi);
        contexte.Corrections.Add(validee);
        contexte.Uow.Get<HsValidee>().Items.Add(new HsValidee
        {
            BddSage = "SAGE_TEST",
            Matricules = "001",
            DateDeb = Lundi,
            DateFin = Lundi.AddDays(6),
            ValidationHs = true
        });

        var resultat = await contexte.Traiter(Lundi, Lundi);

        Assert.Contains(validee, contexte.Corrections);
        Assert.Equal(1, resultat.JoursIgnores);
    }
}
