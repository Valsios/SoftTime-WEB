using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Construction de la base analytique « model-ready » à partir des tables fonctionnelles.
///
/// Le principe est le même que les états : les labels sont produits par les règles existantes
/// (<see cref="AbsenceReportRules"/>, <see cref="OvertimeCalculator"/>), jamais par une
/// réimplémentation. Les features sont calculées avec les données connues avant la date cible
/// (as-of J-1) ; le planning du jour et les fériés sont autorisés car connus à l'avance.
/// </summary>
public interface IMlDatasetService
{
    /// <summary>
    /// Reconstruit les lignes jour/semaine de la plage. <paramref name="persist"/> à false permet
    /// de réutiliser exactement le même calcul pour l'inférence (planning futur, sans écriture).
    /// </summary>
    Task<MlDatasetBuildResult> BuildAsync(DateTime from, DateTime to, bool persist, CancellationToken ct = default);

    /// <summary>Lignes d'inférence (planning futur) réutilisées par <see cref="MlPredictionService"/>.</summary>
    Task<IReadOnlyList<T_ML_SALARIE_JOUR>> BuildInferenceDailyAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Lignes d'inférence hebdomadaires (semaines futures).</summary>
    Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> BuildInferenceWeeklyAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
