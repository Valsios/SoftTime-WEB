using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Port de persistance de la base analytique <c>ml</c>. L'implémentation EF est dans
/// Infrastructure ; les tests unitaires peuvent fournir une doublure en mémoire.
/// </summary>
public interface IMlStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task ReplaceDailyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<MlSalarieJour> rows, CancellationToken ct = default);

    Task ReplaceWeeklyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<MlSalarieSemaine> rows, CancellationToken ct = default);

    Task<IReadOnlyList<MlSalarieJour>> GetDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<MlSalarieSemaine>> GetWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Charge au plus <paramref name="maxRows"/> lignes récentes pour l'entraînement.</summary>
    Task<IReadOnlyList<MlSalarieJour>> GetDailyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default);

    Task<IReadOnlyList<MlSalarieSemaine>> GetWeeklyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default);

    Task SaveModelAsync(MlModele model, CancellationToken ct = default);

    Task<MlModele?> GetActiveModelAsync(string bddSage, MlModelType type, CancellationToken ct = default);

    Task<IReadOnlyList<MlModele>> ListModelsAsync(string bddSage, CancellationToken ct = default);

    Task ReplacePredictionsAsync(
        MlPeriod periode, MlModelType type, IReadOnlyCollection<MlPrediction> rows, CancellationToken ct = default);

    Task<IReadOnlyList<MlPrediction>> GetPredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, CancellationToken ct = default);

    Task ArchiveActiveModelsAsync(string bddSage, MlModelType type, CancellationToken ct = default);

    Task PurgeModelsAsync(string bddSage, MlModelType type, int keep, CancellationToken ct = default);
}
