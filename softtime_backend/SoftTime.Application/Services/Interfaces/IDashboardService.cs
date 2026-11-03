using System.Globalization;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="DashboardService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(PeriodRequest filter, string? categorie, string? periode, CancellationToken ct = default);

    /// <summary>Valeurs distinctes proposées par les filtres de la page (département, catégorie).</summary>
    Task<DashboardFiltersDto> GetFiltersAsync(CancellationToken ct = default);
}
