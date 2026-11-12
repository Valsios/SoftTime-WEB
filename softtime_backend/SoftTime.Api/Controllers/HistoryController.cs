using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.History;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Archives des suppressions : ce qui a été retiré de <c>T_POINTAGE</c>, <c>T_HEUREANOMALIE</c>
/// et <c>T_HSExoImp</c>. Consultation et purge, rattachées à la traçabilité comme le journal d'audit.
/// Le filtre de période porte sur la <b>date de suppression</b> de l'archive.
/// </summary>
[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[RequirePermission(Droit.Traceability, "TRACE:AUDIT")]
[Route("api/v1/history")]
[Tags("History")]
public class HistoryController : ApiControllerBase
{
    private readonly IHistoryQueryService _query;
    private readonly IHistoryPurgeService _purge;

    public HistoryController(IHistoryQueryService query, IHistoryPurgeService purge)
    {
        _query = query;
        _purge = purge;
    }

    [HttpGet("pointages")]
    public async Task<IActionResult> PointagesAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _query.ListPointagesAsync(filter, ct));

    [RequireAdmin]
    [HttpDelete("pointages/{id}")]
    public async Task<IActionResult> DeletePointageAsync(decimal id, CancellationToken ct)
        => await _purge.DeletePointagesAsync(id, ct) == 0 ? NotFound() : NoContent();

    [RequireAdmin]
    [HttpDelete("pointages")]
    public async Task<IActionResult> PurgePointagesAsync(CancellationToken ct)
        => Ok(await _purge.DeleteAllPointagesAsync(ct));

    [HttpGet("anomalies")]
    public async Task<IActionResult> AnomaliesAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _query.ListAnomaliesAsync(filter, ct));

    [RequireAdmin]
    [HttpDelete("anomalies/{id}")]
    public async Task<IActionResult> DeleteAnomalieAsync(decimal id, CancellationToken ct)
        => await _purge.DeleteAnomaliesAsync(id, ct) == 0 ? NotFound() : NoContent();

    [RequireAdmin]
    [HttpDelete("anomalies")]
    public async Task<IActionResult> PurgeAnomaliesAsync(CancellationToken ct)
        => Ok(await _purge.DeleteAllAnomaliesAsync(ct));

    [HttpGet("hs")]
    public async Task<IActionResult> HsAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _query.ListHsAsync(filter, ct));

    [RequireAdmin]
    [HttpDelete("hs/{id}")]
    public async Task<IActionResult> DeleteHsAsync(decimal id, CancellationToken ct)
        => await _purge.DeleteHsAsync(id, ct) == 0 ? NotFound() : NoContent();

    [RequireAdmin]
    [HttpDelete("hs")]
    public async Task<IActionResult> PurgeHsAsync(CancellationToken ct)
        => Ok(await _purge.DeleteAllHsAsync(ct));

    /// <summary>Archive des pointages paginée (page bornée, total serveur).</summary>
    [HttpGet("pointages/paged")]
    public async Task<IActionResult> PointagesPagedAsync(
        [FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _query.ListPointagesPagedAsync(filter, page, pageSize, ct));

    /// <summary>Archive des anomalies paginée.</summary>
    [HttpGet("anomalies/paged")]
    public async Task<IActionResult> AnomaliesPagedAsync(
        [FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _query.ListAnomaliesPagedAsync(filter, page, pageSize, ct));

    /// <summary>Archive des calculs HS paginée.</summary>
    [HttpGet("hs/paged")]
    public async Task<IActionResult> HsPagedAsync(
        [FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _query.ListHsPagedAsync(filter, page, pageSize, ct));
}
