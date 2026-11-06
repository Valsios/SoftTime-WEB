using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Application.Abstractions;

public interface IMlDatasetService
{
    /// <summary>
    /// Reconstruit les lignes jour/semaine de la plage. <paramref name="persist"/> à false permet
    /// de réutiliser exactement le même calcul pour l'inférence (planning futur, sans écriture).
    /// </summary>
    Task<MlDatasetBuildResult> BuildAsync(DateTime from, DateTime to, bool persist, CancellationToken ct = default);

    /// <summary>Lignes d'inférence (planning futur) réutilisées par <see cref="MlPredictionService"/>.</summary>
    Task<IReadOnlyList<MlSalarieJour>> BuildInferenceDailyAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Lignes d'inférence hebdomadaires (semaines futures).</summary>
    Task<IReadOnlyList<MlSalarieSemaine>> BuildInferenceWeeklyAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
