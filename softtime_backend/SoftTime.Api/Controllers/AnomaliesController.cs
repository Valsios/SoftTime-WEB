using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/anomalies")]
[Tags("Anomalies")]
public class AnomaliesController : ApiControllerBase
{
    private readonly IAnomalyService _svc;
    public AnomaliesController(IAnomalyService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListAnomaliesAsync(filter, ct));

    /// <summary>Même liste que <c>GET /api/anomalies</c>, paginée (page bornée, total serveur).</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync([FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListAnomaliesPagedAsync(filter, page, pageSize, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOneAsync(decimal id, CancellationToken ct)
    {
        var dto = await _svc.GetAnomalyAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Arbitrage : heures retenues + drapeaux d'absence / férié.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(decimal id, [FromBody] AnomalyUpdateDto dto, CancellationToken ct)
        => Ok(await _svc.UpdateAnomalyAsync(id, dto, ct));

    /// <summary>Validation en masse : les anomalies deviennent des heures corrigées puis sont supprimées.</summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateAsync([FromBody] AnomalyValidationRequest request, CancellationToken ct)
        => Ok(await _svc.ValidateAnomaliesAsync(request.Ids, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(decimal id, CancellationToken ct)
    {
        await _svc.DeleteAnomalyAsync(id, ct);
        return NoContent();
    }
}
