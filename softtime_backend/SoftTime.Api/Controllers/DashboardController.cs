using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Tableau de bord : KPI de la période, absences, retards, répartition et assiduité.
/// Accessible à tout utilisateur authentifié — c'est la page d'accueil de l'application.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
[Tags("Dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _svc;

    public DashboardController(IDashboardService svc) => _svc = svc;

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string? matriculeFrom,
        [FromQuery] string? matriculeTo,
        [FromQuery] string? departement,
        [FromQuery] string? categorie,
        [FromQuery] string? periode,
        CancellationToken ct)
        => Ok(await _svc.GetStatsAsync(
            new PeriodRequest(matriculeFrom, matriculeTo, from, to, departement),
            categorie,
            string.IsNullOrWhiteSpace(periode) ? "mensuelle" : periode,
            ct));

    /// <summary>Valeurs proposées par les filtres (départements, catégories de personnel).</summary>
    [HttpGet("filters")]
    public async Task<IActionResult> Filters(CancellationToken ct)
        => Ok(await _svc.GetFiltersAsync(ct));
}
