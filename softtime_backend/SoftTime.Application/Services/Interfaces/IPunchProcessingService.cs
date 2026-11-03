using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="PunchProcessingService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IPunchProcessingService
{
    Task<PreprocessPunchesResultDto> ProcessAsync(PeriodRequest request, CancellationToken ct = default);
}
