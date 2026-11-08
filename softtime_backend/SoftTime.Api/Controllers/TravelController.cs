using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Other)]
[Route("api/v1/travel-expenses")]
[Tags("TravelExpenses")]
public class TravelController : ControllerBase
{
    private readonly ITravelExpenseService _svc;
    public TravelController(ITravelExpenseService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ListTravelAsync(request, ct));

    /// <summary>Même liste que <c>GET /api/travel-expenses</c>, paginée.</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> ListPaged(
        [FromQuery] PeriodRequest request, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListTravelPagedAsync(request, page, pageSize, ct));

    [HttpPost("compute")]
    public async Task<IActionResult> Compute([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ComputeTravelAsync(request, ct));

    /// <summary>Pousse les totaux de frais de déplacement de la période dans le cumul SAGE (T_CUMSAL).</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.SyncTravelAsync(request, ct));
}
