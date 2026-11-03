using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="MlPredictionService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IMlPredictionService
{
    /// <summary>Recalcule les prédictions de l'horizon pour la société active.</summary>
    Task<MlPipelineResultDto> GenerateAsync(CancellationToken ct = default);

    Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MlAbsenceForecastDto>> GetAbsenceForecastsAsync(DateTime from, DateTime to, string? departement, int? categorie, CancellationToken ct = default);

    Task<IReadOnlyList<MlHsForecastDto>> GetHsForecastsAsync(DateTime semaine, CancellationToken ct = default);
}
