using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="ClosingService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IClosingService
{
    /// <summary>
    /// Analyse d'une période. Sans options de règles dans la demande, les réglages « Closing »
    /// s'appliquent (tolérance, semaines validées, gravités, poids, seuil de statut).
    /// </summary>
    Task<ClosingAnalysisDto> AnalyserAsync(ClosingAnalysisRequest request, CancellationToken ct = default);
}
