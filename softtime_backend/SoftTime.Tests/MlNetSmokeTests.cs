using SoftTime.Domain.Models.Ml;
using SoftTime.Infrastructure.MachineLearning;

namespace SoftTime.Tests;

/// <summary>
/// Test de fumée ML.NET : le pipeline réel (FastTree) s'entraîne, s'évalue et prédit sur un
/// petit jeu synthétique. Il protège contre les régressions d'API et de sérialisation du modèle.
/// </summary>
public class MlNetSmokeTests
{
    [Fact]
    public void Le_modele_absence_s_entraine_et_predit()
    {
        var random = new Random(42);
        var samples = Enumerable.Range(0, 300).Select(i =>
        {
            var risque = i % 2 == 0;
            var label = risque ? random.NextDouble() > 0.15 : random.NextDouble() > 0.85;
            return new MlAbsenceSample
            {
                Label = label,
                JourSemaine = i % 5,
                SemaineIso = (i % 50) + 1,
                Mois = (i % 12) + 1,
                JourDuMois = (i % 28) + 1,
                Absences7j = risque ? 2 : 0,
                Absences30j = risque ? 8 : 0,
                Absences90j = risque ? 20 : 1,
                TauxAbsence30j = risque ? 0.45f : 0.02f,
                JoursDepuisDerniereAbsence = risque ? 3 : 60,
                JoursTravailles7j = 5,
                HeuresPlanifiees = 8,
                PlanningConnu = true,
                EffectifPrevuJour = 10,
                HeuresSemaineContrat = 40
            };
        }).ToList();

        var trainer = new MlNetTrainer();
        var result = trainer.TrainAbsence(samples.Take(240).ToList(), samples.Skip(240).ToList(), "FastTree");

        Assert.True(result.Accepted);
        Assert.NotNull(result.Model);
        Assert.True(result.Metrics.Auc.HasValue && result.Metrics.Auc.Value > 0.5,
            $"AUC inattendue : {result.Metrics.Auc}");
        Assert.True(result.Metrics.TestPositives > 0);

        var predictions = new MlNetPredictor().PredictAbsence(result.Model!, samples.Take(5).ToList());
        Assert.Equal(5, predictions.Count);
        Assert.All(predictions, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Les_modeles_hs_s_entrainent_et_predisent()
    {
        var samples = Enumerable.Range(0, 240).Select(i =>
        {
            var montant = i % 4 == 0 ? 4f : 0f;
            return new MlHsSample
            {
                HsPositive = montant > 0,
                HsAmount = montant,
                SemaineIso = (i % 50) + 1,
                Mois = (i % 12) + 1,
                HeuresPlanifiees = 40,
                HeuresTravaillees = 40,
                NbJoursPlanifies = 5,
                NbJoursTravailles = 5,
                HsMoyenne4Semaines = montant > 0 ? 3f : 0f,
                HsMoyenne8Semaines = montant > 0 ? 2f : 0f,
                HeuresSemaineContrat = 40
            };
        }).ToList();

        var trainer = new MlNetTrainer();
        var train = samples.Take(192).ToList();
        var test = samples.Skip(192).ToList();

        var probabilite = trainer.TrainHsProbability(train, test, "FastTree");
        var montant = trainer.TrainHsAmount(train, test, "FastTree");

        Assert.True(probabilite.Accepted);
        Assert.NotNull(probabilite.Model);
        Assert.True(montant.Accepted);
        Assert.NotNull(montant.Model);

        var predictions = new MlNetPredictor().PredictHsAmount(montant.Model!, test.Take(5).ToList());
        Assert.Equal(5, predictions.Count);
        Assert.All(predictions, p => Assert.False(float.IsNaN(p)));
    }
}
