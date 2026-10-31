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
        IReadOnlyCollection<T_ML_SALARIE_JOUR> rows, CancellationToken ct = default);

    Task ReplaceWeeklyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<T_ML_SALARIE_SEMAINE> rows, CancellationToken ct = default);

    Task<IReadOnlyList<T_ML_SALARIE_JOUR>> GetDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> GetWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Charge au plus <paramref name="maxRows"/> lignes récentes pour l'entraînement.</summary>
    Task<IReadOnlyList<T_ML_SALARIE_JOUR>> GetDailyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default);

    Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> GetWeeklyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default);

    Task SaveModelAsync(T_ML_MODELE model, CancellationToken ct = default);

    Task<T_ML_MODELE?> GetActiveModelAsync(string bddSage, MlModelType type, CancellationToken ct = default);

    Task<IReadOnlyList<T_ML_MODELE>> ListModelsAsync(string bddSage, CancellationToken ct = default);

    Task ReplacePredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, int modelId,
        IReadOnlyCollection<T_ML_PREDICTION> rows, CancellationToken ct = default);

    Task<IReadOnlyList<T_ML_PREDICTION>> GetPredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, CancellationToken ct = default);

    Task ArchiveActiveModelsAsync(string bddSage, MlModelType type, CancellationToken ct = default);

    Task PurgeModelsAsync(string bddSage, MlModelType type, int keep, CancellationToken ct = default);
}
