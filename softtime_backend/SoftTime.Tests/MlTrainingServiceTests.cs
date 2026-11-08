using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// Vérifie la politique d'activation des modèles HS : données insuffisantes → baseline, modèle sous la porte
/// qualité → rejeté sans casser l'ancien modèle, modèle au-dessus → activé.
/// </summary>
public class MlTrainingServiceTests
{
    private static readonly string Sage = "SAGE_TEST";

    [Fact]
    public async Task TrainAsync_DonneesInsuffisantes_ResteEnBaseline()
    {
        // Arrange
        var store = new FakeMlStore();
        var sut = new MlTrainingService(store, new FakeMlTrainer(), Options.Create(TrainingOptions()), NullLogger<MlTrainingService>.Instance);

        // Act
        var outcome = await sut.TrainAsync(Sage);

        // Assert
        outcome.Etat.Should().Be("Baseline");
        outcome.HsProbabilite.Should().BeNull();
        outcome.HsMontant.Should().BeNull();
        store.Models.Should().NotBeEmpty();
        store.Models.Should().AllSatisfy(m => m.Statut.Should().Be(MlModelStatus.Rejete));
    }

    [Fact]
    public async Task TrainAsync_ModeleAuDessusDeLaBaseline_EstActive()
    {
        // Arrange
        var store = new FakeMlStore();
        Seed(store);
        var trainer = new FakeMlTrainer
        {
            AmountResult = new MlTrainingResult(true, "Fake", new byte[] { 1, 2, 3 },
                new MlMetrics(Mae: 0.5, TestRows: 50, TestPositives: 10), null)
        };
        var sut = new MlTrainingService(store, trainer, Options.Create(TrainingOptions()), NullLogger<MlTrainingService>.Instance);

        // Act
        var outcome = await sut.TrainAsync(Sage);

        // Assert
        outcome.Etat.Should().Be("Pret");
        (outcome.HsProbabilite is { EstActif: true }).Should().BeTrue();
        (outcome.HsMontant is { EstActif: true }).Should().BeTrue();
        store.Models.Should().Contain(m => m.TypeModele == MlModelTypes.HsMontant && m.EstActif);
        // La prévision d'absence n'est plus entraînée.
        store.Models.Should().NotContain(m => m.TypeModele == MlModelTypes.Absence);
    }

    [Fact]
    public async Task TrainAsync_ModeleSousLaBaseline_EstRejeteEtAncienConserve()
    {
        // Arrange
        var store = new FakeMlStore();
        Seed(store);
        store.Models.Add(new MlModele
        {
            BddSage = Sage,
            TypeModele = MlModelTypes.HsMontant,
            Version = "ancien",
            Algorithme = "Fake",
            Statut = MlModelStatus.Active,
            EstActif = true,
            EntraineLe = DateTime.Now.AddDays(-10),
            PeriodeDebut = DateTime.Today.AddMonths(-6),
            PeriodeFin = DateTime.Today.AddDays(-1),
            NbLignes = 100,
            MetriquesJson = "{}",
            SchemaHash = "ancien",
            ModeleBinaire = new byte[] { 9 }
        });
        var trainer = new FakeMlTrainer
        {
            AmountResult = new MlTrainingResult(true, "Fake", new byte[] { 1, 2, 3 },
                new MlMetrics(Mae: 2.0, TestRows: 50, TestPositives: 10), null)
        };
        var sut = new MlTrainingService(store, trainer, Options.Create(TrainingOptions()), NullLogger<MlTrainingService>.Instance);

        // Act
        var outcome = await sut.TrainAsync(Sage);

        // Assert
        outcome.Etat.Should().Be("Baseline");
        (outcome.HsMontant is { EstActif: true }).Should().BeFalse();
        store.Models.Count(m => m.TypeModele == MlModelTypes.HsMontant && m.EstActif).Should().Be(1);
        store.Models.Single(m => m.TypeModele == MlModelTypes.HsMontant && m.EstActif).Version.Should().Be("ancien");
        store.Models.Should().Contain(m => m.TypeModele == MlModelTypes.HsMontant && m.Statut == MlModelStatus.Rejete);
    }

    private static MlOptions TrainingOptions() => new()
    {
        HistoryMonths = 18,
        TestWindowWeeks = 8,
        MinDailyRows = 5,
        MinAbsencePositives = 2,
        MinWeeklyRows = 2,
        MinTestPositives = 1,
        ModelRetentionPerType = 5,
        AbsenceMinAuc = 0.55,
        AbsenceAucImprovement = 0.02,
        OvertimeMaeImprovement = 0.05
    };

    private static void Seed(FakeMlStore store)
    {
        var to = DateTime.Today.AddDays(-1);

        for (var i = 0; i < 120; i++)
        {
            var date = to.AddDays(-i);
            store.Daily.Add(new MlSalarieJour
            {
                BddSage = Sage,
                Matricule = "001",
                Date = date,
                EstJourPlanifie = true,
                PlanningConnu = true,
                AbsenceNonJustifiee = i % 10 == 0,
                TauxAbsence30j = 0.1,
                HeuresPlanifiees = 8m,
                JourSemaine = (byte)date.DayOfWeek,
                SemaineIso = (byte)ISOWeek.GetWeekOfYear(date),
                AnneeIso = (short)ISOWeek.GetYear(date),
                Mois = (byte)date.Month,
                JourDuMois = (byte)date.Day,
                Departement = "Atelier",
                CategorieId = 10,
                HeuresSemaineContrat = 40m,
                MultiPointage = true,
                VersionSchema = MlSchema.Version,
                SourceGenereeLe = DateTime.Now
            });
        }

        var lundi = MondayOf(to);
        for (var i = 0; i < 60; i++)
        {
            var monday = lundi.AddDays(-7 * i);
            store.Weekly.Add(new MlSalarieSemaine
            {
                BddSage = Sage,
                Matricule = "001",
                SemaineDebut = monday,
                SemaineIso = (byte)ISOWeek.GetWeekOfYear(monday),
                AnneeIso = (short)ISOWeek.GetYear(monday),
                HeuresPlanifiees = 40m,
                HeuresTravaillees = 40m,
                NbJoursPlanifies = 5,
                NbJoursTravailles = 5,
                Hs = i % 3 == 0 ? 2m : 0m,
                HsValidee = true,
                HsMoyenne4Semaines = 1m,
                HsMoyenne8Semaines = 1m,
                Departement = "Atelier",
                CategorieId = 10,
                HeuresSemaineContrat = 40m,
                MultiPointage = true,
                VersionSchema = MlSchema.Version,
                SourceGenereeLe = DateTime.Now
            });
        }
    }

    private static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));
}
