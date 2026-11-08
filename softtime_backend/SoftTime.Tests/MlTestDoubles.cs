using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Tests;

/// <summary>Doublure en mémoire de la base analytique pour les tests de services ML.</summary>
internal sealed class FakeMlStore : IMlStore
{
    public List<MlSalarieJour> Daily { get; } = new();
    public List<MlSalarieSemaine> Weekly { get; } = new();
    public List<MlModele> Models { get; } = new();
    public List<MlPrediction> Predictions { get; } = new();

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task ReplaceDailyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<MlSalarieJour> rows, CancellationToken ct = default)
    {
        Daily.RemoveAll(r => r.BddSage == bddSage && r.Date >= from.Date && r.Date <= to.Date);
        Daily.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task ReplaceWeeklyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<MlSalarieSemaine> rows, CancellationToken ct = default)
    {
        Weekly.RemoveAll(r => r.BddSage == bddSage && r.SemaineDebut >= from.Date && r.SemaineDebut <= to.Date);
        Weekly.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MlSalarieJour>> GetDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MlSalarieJour>>(
            Daily.Where(r => r.BddSage == bddSage && r.Date >= from.Date && r.Date <= to.Date).ToList());

    public Task<IReadOnlyList<MlSalarieSemaine>> GetWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MlSalarieSemaine>>(
            Weekly.Where(r => r.BddSage == bddSage && r.SemaineDebut >= from.Date && r.SemaineDebut <= to.Date).ToList());

    public Task<IReadOnlyList<MlSalarieJour>> GetDailyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MlSalarieJour>>(
            Daily.Where(r => r.BddSage == bddSage && r.Date >= from.Date && r.Date <= to.Date)
                .OrderByDescending(r => r.Date).Take(Math.Max(1, maxRows)).OrderBy(r => r.Date).ToList());

    public Task<IReadOnlyList<MlSalarieSemaine>> GetWeeklyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MlSalarieSemaine>>(
            Weekly.Where(r => r.BddSage == bddSage && r.SemaineDebut >= from.Date && r.SemaineDebut <= to.Date)
                .OrderByDescending(r => r.SemaineDebut).Take(Math.Max(1, maxRows)).OrderBy(r => r.SemaineDebut).ToList());

    public Task SaveModelAsync(MlModele model, CancellationToken ct = default)
    {
        model.Id = Models.Count + 1;
        Models.Add(model);
        return Task.CompletedTask;
    }

    public Task<MlModele?> GetActiveModelAsync(string bddSage, MlModelType type, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return Task.FromResult(Models.LastOrDefault(m =>
            m.BddSage == bddSage && m.TypeModele == storageType && m.EstActif));
    }

    public Task<IReadOnlyList<MlModele>> ListModelsAsync(string bddSage, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MlModele>>(
            Models.Where(m => m.BddSage == bddSage).OrderByDescending(m => m.EntraineLe).ToList());

    public Task ReplacePredictionsAsync(
        MlPeriod periode, MlModelType type, IReadOnlyCollection<MlPrediction> rows, CancellationToken ct = default)
    {
        var (bddSage, from, to) = periode;
        var storageType = MlModelTypes.ToStorage(type);
        Predictions.RemoveAll(p => p.BddSage == bddSage
                                   && p.TypeModele == storageType
                                   && p.Cible >= from.Date
                                   && p.Cible <= to.Date);
        Predictions.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MlPrediction>> GetPredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return Task.FromResult<IReadOnlyList<MlPrediction>>(
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
