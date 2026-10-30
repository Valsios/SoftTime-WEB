using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Tests;

/// <summary>Doublure en mémoire de la base analytique pour les tests de services ML.</summary>
internal sealed class FakeMlStore : IMlStore
{
    public List<T_ML_SALARIE_JOUR> Daily { get; } = new();
    public List<T_ML_SALARIE_SEMAINE> Weekly { get; } = new();
    public List<T_ML_MODELE> Models { get; } = new();
    public List<T_ML_PREDICTION> Predictions { get; } = new();

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task ReplaceDailyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<T_ML_SALARIE_JOUR> rows, CancellationToken ct = default)
    {
        Daily.RemoveAll(r => r.BddSage == bddSage && r.Date >= from.Date && r.Date <= to.Date);
        Daily.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task ReplaceWeeklyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<T_ML_SALARIE_SEMAINE> rows, CancellationToken ct = default)
    {
        Weekly.RemoveAll(r => r.BddSage == bddSage && r.SemaineDebut >= from.Date && r.SemaineDebut <= to.Date);
        Weekly.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<T_ML_SALARIE_JOUR>> GetDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T_ML_SALARIE_JOUR>>(
            Daily.Where(r => r.BddSage == bddSage && r.Date >= from.Date && r.Date <= to.Date).ToList());

    public Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> GetWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T_ML_SALARIE_SEMAINE>>(
            Weekly.Where(r => r.BddSage == bddSage && r.SemaineDebut >= from.Date && r.SemaineDebut <= to.Date).ToList());

    public Task<IReadOnlyList<T_ML_SALARIE_JOUR>> GetDailyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T_ML_SALARIE_JOUR>>(
            Daily.Where(r => r.BddSage == bddSage && r.Date >= from.Date && r.Date <= to.Date)
                .OrderByDescending(r => r.Date).Take(Math.Max(1, maxRows)).OrderBy(r => r.Date).ToList());

    public Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> GetWeeklyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T_ML_SALARIE_SEMAINE>>(
            Weekly.Where(r => r.BddSage == bddSage && r.SemaineDebut >= from.Date && r.SemaineDebut <= to.Date)
                .OrderByDescending(r => r.SemaineDebut).Take(Math.Max(1, maxRows)).OrderBy(r => r.SemaineDebut).ToList());

    public Task SaveModelAsync(T_ML_MODELE model, CancellationToken ct = default)
    {
        model.Id = Models.Count + 1;
        Models.Add(model);
        return Task.CompletedTask;
    }

    public Task<T_ML_MODELE?> GetActiveModelAsync(string bddSage, MlModelType type, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return Task.FromResult(Models.LastOrDefault(m =>
            m.BddSage == bddSage && m.TypeModele == storageType && m.EstActif));
    }

    public Task<IReadOnlyList<T_ML_MODELE>> ListModelsAsync(string bddSage, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T_ML_MODELE>>(
            Models.Where(m => m.BddSage == bddSage).OrderByDescending(m => m.EntraineLe).ToList());

    public Task ReplacePredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, int modelId,
        IReadOnlyCollection<T_ML_PREDICTION> rows, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        Predictions.RemoveAll(p => p.BddSage == bddSage
                                   && p.TypeModele == storageType
                                   && p.ModeleId == modelId
                                   && p.Cible >= from.Date
                                   && p.Cible <= to.Date);
        Predictions.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<T_ML_PREDICTION>> GetPredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return Task.FromResult<IReadOnlyList<T_ML_PREDICTION>>(
            Predictions.Where(p => p.BddSage == bddSage
                                   && p.TypeModele == storageType
                                   && p.Cible >= from.Date
                                   && p.Cible <= to.Date).ToList());
    }

    public Task ArchiveActiveModelsAsync(string bddSage, MlModelType type, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        foreach (var model in Models.Where(m => m.BddSage == bddSage && m.TypeModele == storageType && m.EstActif))
        {
            model.EstActif = false;
            model.Statut = MlModelStatus.Archive;
        }
        return Task.CompletedTask;
    }

    public Task PurgeModelsAsync(string bddSage, MlModelType type, int keep, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Entraîneur factice pour tester la porte qualité sans ML.NET.</summary>
internal sealed class FakeMlTrainer : IMlTrainer
{
    public string FeatureSchemaHash => "hash-test";
    public MlTrainingResult AbsenceResult { get; set; } = Accepted(0.90);
    public MlTrainingResult ProbabilityResult { get; set; } = Accepted(0.85);
    public MlTrainingResult AmountResult { get; set; } = AcceptedAmount(1.5);

    public MlTrainingResult TrainAbsence(
        IReadOnlyList<MlAbsenceSample> train, IReadOnlyList<MlAbsenceSample> test,
        string algorithm, CancellationToken ct = default)
        => AbsenceResult;

    public MlTrainingResult TrainHsProbability(
        IReadOnlyList<MlHsSample> train, IReadOnlyList<MlHsSample> test,
        string algorithm, CancellationToken ct = default)
        => ProbabilityResult;

    public MlTrainingResult TrainHsAmount(
        IReadOnlyList<MlHsSample> train, IReadOnlyList<MlHsSample> test,
        string algorithm, CancellationToken ct = default)
        => AmountResult;

    private static MlTrainingResult Accepted(double auc)
        => new(true, "Fake", new byte[] { 1, 2, 3 },
            new MlMetrics(Auc: auc, TestRows: 50, TestPositives: 20), null);

    private static MlTrainingResult AcceptedAmount(double mae)
        => new(true, "Fake", new byte[] { 1, 2, 3 },
            new MlMetrics(Mae: mae, TestRows: 50, TestPositives: 20), null);
}
