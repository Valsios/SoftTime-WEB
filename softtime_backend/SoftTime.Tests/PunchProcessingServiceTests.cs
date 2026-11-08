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
        // Arrange
        var sut = new Contexte();

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        resultat.CorrectionsCreees.Should().Be(0);
        var anomalie = sut.Anomalies.Should().ContainSingle().Which;
        anomalie.AbsAm.Should().BeTrue();
        anomalie.AbsPm.Should().BeTrue();
        anomalie.H1.Should().BeNull();
        anomalie.H2.Should().BeNull();
    }

    [Fact]
    public async Task Mono_UnPointage_CreeUneAnomalieAvecH1EtH2()
    {
        // Arrange
        var sut = new Contexte();
        sut.Pointage(Lundi, 8);

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        var anomalie = sut.Anomalies.Should().ContainSingle().Which;
        anomalie.H1.Should().Be(new TimeSpan(8, 0, 0));
        anomalie.H2.Should().Be(new TimeSpan(8, 0, 0));
        anomalie.AbsAm.Should().BeNull();
        anomalie.AbsPm.Should().BeNull();
    }

    [Fact]
    public async Task Mono_DeuxPointages_CreeUneCorrectionPremierDernier()
    {
        // Arrange
        var sut = new Contexte();
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 17);

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        var correction = sut.Corrections.Should().ContainSingle().Which;
        correction.HEntree.Should().Be(new TimeSpan(8, 0, 0));
        correction.HSortie.Should().Be(new TimeSpan(17, 0, 0));
        correction.DatePointageIn.Should().Be(Lundi.Date);
        correction.DatePointageOut.Should().Be(Lundi.Date);
        correction.ValiderCorrection.Should().BeTrue();
        correction.Origine.Should().Be(PunchProcessingConfiguration.OrigineAuto);
        correction.Ferier.Should().BeNull();
        sut.Anomalies.Should().BeEmpty();
        resultat.CorrectionsCreees.Should().Be(1);
    }

    [Fact]
    public async Task Mono_Ferie_DeuxPointages_DrapeauxFeries()
    {
        // Arrange
        var sut = new Contexte();
        sut.Ferie(Lundi);
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 17);

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        var correction = sut.Corrections.Should().ContainSingle().Which;
        correction.Ferier.Should().BeTrue();
        correction.FerierAm.Should().BeTrue();
        correction.FerierPm.Should().BeTrue();
    }

    [Fact]
    public async Task Mono_Ferie_SansPointage_NeCreeRien()
    {
        // Arrange
        var sut = new Contexte();
        sut.Ferie(Lundi);

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        sut.Anomalies.Should().BeEmpty();
        resultat.AnomaliesCreees.Should().Be(0);
    }

    [Fact]
    public async Task Mono_Samedi_DeuxPointages_CreeUneCorrectionSansFerie()
    {
        // Arrange
        var sut = new Contexte();
        sut.Pointage(Samedi, 8);
        sut.Pointage(Samedi, 17);

        // Act
        var resultat = await sut.Traiter(Samedi, Samedi);

        // Assert
        var correction = sut.Corrections.Should().ContainSingle().Which;
        correction.Ferier.Should().BeNull();
        resultat.CorrectionsCreees.Should().Be(1);
    }

    [Fact]
    public async Task Mono_Samedi_SansPointage_NeCreeRien()
    {
        // Arrange
        var sut = new Contexte();

        // Act
        await sut.Traiter(Samedi, Samedi);

        // Assert
        sut.Anomalies.Should().BeEmpty();
    }

    [Fact]
    public async Task Mono_IgnoreLePlanningDeNuit_TraiteChaqueJourSeparement()
    {
        // Arrange
        var mardi = Lundi.AddDays(1);
        var sut = new Contexte();
        sut.Planning(Lundi, Lundi.AddHours(22), mardi.AddHours(6));
        sut.Pointage(Lundi, 22);
        sut.Pointage(mardi, 6);

        // Act
        await sut.Traiter(Lundi, mardi);

        // Assert
        sut.Anomalies.Count.Should().Be(2);
        sut.Corrections.Should().BeEmpty();
    }

    [Fact]
    public async Task Multi_QuatrePointages_DeuxCorrections()
    {
        // Arrange
        var sut = new Contexte(multi: true);
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 12);
        sut.Pointage(Lundi, 13);
        sut.Pointage(Lundi, 17);

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        sut.Corrections.Count.Should().Be(2);
        resultat.CorrectionsCreees.Should().Be(2);
        sut.Corrections[0].HEntree.Should().Be(new TimeSpan(8, 0, 0));
        sut.Corrections[0].HSortie.Should().Be(new TimeSpan(12, 0, 0));
        sut.Corrections[1].HEntree.Should().Be(new TimeSpan(13, 0, 0));
        sut.Corrections[1].HSortie.Should().Be(new TimeSpan(17, 0, 0));
        sut.Anomalies.Should().BeEmpty();
    }

    [Fact]
    public async Task Multi_NombreImpair_CreeUneAnomalieH1H2()
    {
        // Arrange
        var sut = new Contexte(multi: true);
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 13);
        sut.Pointage(Lundi, 17);

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        var anomalie = sut.Anomalies.Should().ContainSingle().Which;
        anomalie.H1.Should().Be(new TimeSpan(8, 0, 0));
        anomalie.H2.Should().Be(new TimeSpan(17, 0, 0));
        sut.Corrections.Should().BeEmpty();
    }

    [Fact]
    public async Task Multi_JourSansPointage_AvecPlanningTravaille_CreeUneAnomalie()
    {
        // Arrange
        var sut = new Contexte(multi: true);
        sut.Planning(Lundi, Lundi.AddHours(8), Lundi.AddHours(17));

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        var anomalie = sut.Anomalies.Should().ContainSingle().Which;
        anomalie.AbsAm.Should().BeTrue();
        anomalie.AbsPm.Should().BeTrue();
    }

    [Fact]
    public async Task Multi_JourSansPointage_SansPlanning_NeCreeRien()
    {
        // Arrange
        var sut = new Contexte(multi: true);

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        sut.Anomalies.Should().BeEmpty();
    }

    [Fact]
    public async Task Multi_OffP_SansPointage_NeCreeRien()
    {
        // Arrange
        var sut = new Contexte(multi: true);
        sut.Planning(Lundi, Lundi.AddHours(8), Lundi.AddHours(17), off: true);

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        sut.Anomalies.Should().BeEmpty();
    }

    [Fact]
    public async Task Multi_Nuit_AvecPlanning_CreeUnePaireSurDeuxJours()
    {
        // Arrange
        var mardi = Lundi.AddDays(1);
        var sut = new Contexte(multi: true);
        sut.Planning(Lundi, Lundi.AddHours(22), mardi.AddHours(6));
        sut.Pointage(Lundi, 22);
        sut.Pointage(mardi, 6);

        // Act
        await sut.Traiter(Lundi, mardi);

        // Assert
        var correction = sut.Corrections.Should().ContainSingle().Which;
        correction.DatePointageIn.Should().Be(Lundi.Date);
        correction.DatePointageOut.Should().Be(mardi.Date);
        correction.HEntree.Should().Be(new TimeSpan(22, 0, 0));
        correction.HSortie.Should().Be(new TimeSpan(6, 0, 0));
        sut.Anomalies.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_CorrectionManuelle_EstProtegee()
    {
        // Arrange
        var sut = new Contexte();
        sut.Uow.Get<HeureCorrigee>().Items.Add(new HeureCorrigee
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
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 17);

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        resultat.JoursIgnores.Should().Be(1);
        sut.Corrections.Should().ContainSingle();
        sut.Corrections[0].Origine.Should().Be(PunchProcessingConfiguration.OrigineManuel);
        sut.Corrections[0].HSortie.Should().Be(new TimeSpan(16, 0, 0));
    }

    [Fact]
    public async Task ProcessAsync_SemaineValidee_EstProtegee()
    {
        // Arrange
        var sut = new Contexte();
        sut.Uow.Get<HsValidee>().Items.Add(new HsValidee
        {
            BddSage = "SAGE_TEST",
            Matricules = "001",
            DateDeb = Lundi.Date,
            ValidationHs = true
        });
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 17);

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        resultat.JoursIgnores.Should().Be(1);
        sut.Corrections.Should().BeEmpty();
    }

    [Fact]
    public async Task Reexecution_Corrections_EstIdempotente()
    {
        // Arrange
        var sut = new Contexte();
        sut.Pointage(Lundi, 8);
        sut.Pointage(Lundi, 17);
        var premier = await sut.Traiter(Lundi, Lundi);

        // Act
        var second = await sut.Traiter(Lundi, Lundi);

        // Assert
        premier.CorrectionsCreees.Should().Be(1);
        second.CorrectionsCreees.Should().Be(1);
        sut.Corrections.Should().ContainSingle();
    }

    [Fact]
    public async Task Reexecution_Anomalies_MetAJourSansDupliquer()
    {
        // Arrange
        var sut = new Contexte();
        sut.Pointage(Lundi, 8);
        var premier = await sut.Traiter(Lundi, Lundi);

        // Act
        var second = await sut.Traiter(Lundi, Lundi);

        // Assert
        premier.AnomaliesCreees.Should().Be(1);
        second.AnomaliesCreees.Should().Be(0);
        sut.Anomalies.Should().ContainSingle();
    }

    [Fact]
    public async Task Mono_Ferie_SansPointage_AvecPlanning_NeCreePasDAnomalie()
    {
        // Arrange
        var sut = new Contexte();
        sut.Ferie(Lundi);
        sut.Planning(Lundi, Lundi.AddHours(8), Lundi.AddHours(17));

        // Act
        await sut.Traiter(Lundi, Lundi);

        // Assert
        sut.Anomalies.Should().BeEmpty();
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
        // Arrange
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

        // Act
        await contexte.Service.ProcessAsync(new PeriodRequest(null, null, Lundi, Lundi, "ATELIER"));

        // Assert
        contexte.Corrections.Should().Contain(autre);
    }

    [Fact]
    public async Task Traitement_SemaineValidee_ConserveSesCorrectionsAuto()
    {
        // Arrange
        var sut = new Contexte();
        var validee = CorrectionAuto("001", Lundi);
        sut.Corrections.Add(validee);
        sut.Uow.Get<HsValidee>().Items.Add(new HsValidee
        {
            BddSage = "SAGE_TEST",
            Matricules = "001",
            DateDeb = Lundi,
            DateFin = Lundi.AddDays(6),
            ValidationHs = true
        });

        // Act
        var resultat = await sut.Traiter(Lundi, Lundi);

        // Assert
        sut.Corrections.Should().Contain(validee);
        resultat.JoursIgnores.Should().Be(1);
    }
}
