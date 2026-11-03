using Microsoft.Extensions.Logging;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models.Ml;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="MlPipelineService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IMlPipelineService
{
    Task<MlPipelineResultDto> RunAsync(bool fullRefresh, bool train, CancellationToken ct = default);
}
