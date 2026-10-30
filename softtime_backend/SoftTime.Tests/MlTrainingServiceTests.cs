using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Tests;

/// <summary>
/// Vérifie la politique d'activation : données insuffisantes → baseline, modèle sous la porte
/// qualité → rejeté sans casser l'ancien modèle, modèle au-dessus → activé.
/// </summary>
public class MlTrainingServiceTests
{
    private static readonly string Sage = "SAGE_TEST";

    [Fact]
    public async Task Donnees_insuffisantes_restent_en_baseline()
    {
        var store = new FakeMlStore();
        var service = new MlTrainingService(store, new FakeMlTrainer(), Options(), NullLogger<MlTrainingService>.Instance);

        var outcome = await service.TrainAsync(Sage);

        Assert.Equal("Baseline", outcome.Etat);
        Assert.Null(outcome.Absence);
        Assert.Null(outcome.HsMontant);
        Assert.NotEmpty(store.Models);
        Assert.All(store.Models, m => Assert.Equal(MlModelStatus.Rejete, m.Statut));
    }

    [Fact]
    public async Task Modele_au_dessus_de_la_baseline_est_active()
    {
        var store = new FakeMlStore();
        Seed(store);
        var trainer = new FakeMlTrainer
        {
            AmountResult = new MlTrainingResult(true, "Fake", new byte[] { 1, 2, 3 },
                new MlMetrics(Mae: 0.5, TestRows: 50, TestPositives: 10), null)
        };
        var service = new MlTrainingService(store, trainer, Options(), NullLogger<MlTrainingService>.Instance);

        var outcome = await service.TrainAsync(Sage);

        Assert.Equal("Pret", outcome.Etat);
        Assert.True(outcome.Absence is { EstActif: true });
        Assert.True(outcome.HsMontant is { EstActif: true });
        Assert.Contains(store.Models, m => m.TypeModele == MlModelTypes.Absence && m.EstActif);
        Assert.Contains(store.Models, m => m.TypeModele == MlModelTypes.HsMontant && m.EstActif);
    }

    [Fact]
    public async Task Modele_sous_la_baseline_est_rejete_et_ancien_modele_conserve()
    {
        var store = new FakeMlStore();
        Seed(store);
        store.Models.Add(new T_ML_MODELE
        {
            BddSage = Sage,
            TypeModele = MlModelTypes.Absence,
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
            AbsenceResult = new MlTrainingResult(true, "Fake", new byte[] { 1, 2, 3 },
                new MlMetrics(Auc: 0.50, TestRows: 50, TestPositives: 10), null)
        };
        var service = new MlTrainingService(store, trainer, Options(), NullLogger<MlTrainingService>.Instance);

        var outcome = await service.TrainAsync(Sage);

        Assert.False(outcome.Absence is { EstActif: true });
        Assert.Equal(1, store.Models.Count(m => m.TypeModele == MlModelTypes.Absence && m.EstActif));
        Assert.Equal("ancien", store.Models.Single(m => m.TypeModele == MlModelTypes.Absence && m.EstActif).Version);
        Assert.Contains(store.Models, m => m.TypeModele == MlModelTypes.Absence && m.Statut == MlModelStatus.Rejete);
    }

    private static MlOptions Options() => new()
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
            store.Daily.Add(new T_ML_SALARIE_JOUR
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
            store.Weekly.Add(new T_ML_SALARIE_SEMAINE
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
                HS = i % 3 == 0 ? 2m : 0m,
                HSValidee = true,
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
