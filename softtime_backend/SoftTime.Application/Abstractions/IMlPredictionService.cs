using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IMlPredictionService
{
    /// <summary>Recalcule les prédictions de l'horizon pour la société active.</summary>
    Task<MlPipelineResultDto> GenerateAsync(CancellationToken ct = default);
}
