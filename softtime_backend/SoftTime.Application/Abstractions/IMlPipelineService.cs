using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Orchestration d'un cycle pour la société active : rafraîchissement de l'ETL (incrémental ou
/// complet par trimestres), entraînement éventuel, puis génération des prédictions.
/// </summary>
public interface IMlPipelineService
{
    Task<MlPipelineResultDto> RunAsync(bool fullRefresh, bool train, CancellationToken ct = default);
}
