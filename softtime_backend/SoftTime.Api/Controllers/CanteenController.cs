using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Other)]
[Route("api/canteen")]
[Tags("Canteen")]
public class CanteenController : ControllerBase
{
    private readonly ICanteenService _svc;
    public CanteenController(ICanteenService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ListCanteenAsync(request, ct));

    /// <summary>Même liste que <c>GET /api/canteen</c>, paginée.</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> ListPaged(
        [FromQuery] PeriodRequest request, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCanteenPagedAsync(request, page, pageSize, ct));

    /// <summary>Anomalies du calcul cantine : pointage contredisant une absence complète.</summary>
    [HttpGet("anomalies")]
    public async Task<IActionResult> Anomalies([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ListCanteenAnomaliesAsync(request, ct));

    /// <summary>Anomalies cantine paginées (calcul complet puis découpage de la page).</summary>
    [HttpGet("anomalies/paged")]
    public async Task<IActionResult> AnomaliesPaged(
        [FromQuery] PeriodRequest request, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCanteenAnomaliesPagedAsync(request, page, pageSize, ct));

    [HttpPost("compute")]
    public async Task<IActionResult> Compute([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ComputeCanteenAsync(request, ct));

    /// <summary>Pousse les totaux cantine de la période dans le cumul SAGE (T_CUMSAL).</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.SyncCanteenAsync(request, ct));

    /// <summary>Purge les cantines calculées sur la période.</summary>
    [HttpDelete]
    public async Task<IActionResult> Purge([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.PurgeCanteenAsync(request, ct));
}
