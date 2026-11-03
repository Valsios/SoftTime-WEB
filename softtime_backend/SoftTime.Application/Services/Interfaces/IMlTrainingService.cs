using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="MlTrainingService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IMlTrainingService
{
    Task<MlTrainingOutcome> TrainAsync(string bddSage, CancellationToken ct = default);
}
