using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/anomalies")]
[Tags("Anomalies")]
public class AnomaliesController : ControllerBase
{
    private readonly TimekeepingService _svc;
    public AnomaliesController(TimekeepingService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListAnomaliesAsync(filter, ct));

    /// <summary>Même liste que <c>GET /api/anomalies</c>, paginée (page bornée, total serveur).</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged([FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListAnomaliesPagedAsync(filter, page, pageSize, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOne(decimal id, CancellationToken ct)
    {
        var dto = await _svc.GetAnomalyAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Arbitrage : heures retenues + drapeaux d'absence / férié.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] AnomalyUpdateDto dto, CancellationToken ct)
        => Ok(await _svc.UpdateAnomalyAsync(id, dto, ct));

    /// <summary>Validation en masse : les anomalies deviennent des heures corrigées puis sont supprimées.</summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] AnomalyValidationRequest request, CancellationToken ct)
        => Ok(await _svc.ValidateAnomaliesAsync(request.Ids, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _svc.DeleteAnomalyAsync(id, ct);
        return NoContent();
    }
}
