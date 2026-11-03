using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="MlDatasetService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
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
