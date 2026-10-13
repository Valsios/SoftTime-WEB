using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Archives des suppressions : ce qui a été retiré de <c>T_POINTAGE</c>, <c>T_HEUREANOMALIE</c>
/// et <c>T_HSExoImp</c>. Consultation et purge, rattachées à la traçabilité comme le journal d'audit.
/// Le filtre de période porte sur la <b>date de suppression</b> de l'archive.
/// </summary>
[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[Route("api/history")]
[Tags("History")]
public class HistoryController : ControllerBase
{
    private readonly HistoryArchiveService _svc;

    public HistoryController(HistoryArchiveService svc) => _svc = svc;

    [HttpGet("pointages")]
    public async Task<IActionResult> Pointages([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListPointagesAsync(filter, ct));

    [HttpDelete("pointages/{id}")]
    public async Task<IActionResult> DeletePointage(decimal id, CancellationToken ct)
        => await _svc.DeletePointagesAsync(id, ct) == 0 ? NotFound() : NoContent();

    [HttpDelete("pointages")]
    public async Task<IActionResult> PurgePointages(CancellationToken ct)
        => Ok(await _svc.DeleteAllPointagesAsync(ct));

    [HttpGet("anomalies")]
    public async Task<IActionResult> Anomalies([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListAnomaliesAsync(filter, ct));

    [HttpDelete("anomalies/{id}")]
    public async Task<IActionResult> DeleteAnomalie(decimal id, CancellationToken ct)
        => await _svc.DeleteAnomaliesAsync(id, ct) == 0 ? NotFound() : NoContent();

    [HttpDelete("anomalies")]
    public async Task<IActionResult> PurgeAnomalies(CancellationToken ct)
        => Ok(await _svc.DeleteAllAnomaliesAsync(ct));

    [HttpGet("hs")]
    public async Task<IActionResult> Hs([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListHsAsync(filter, ct));

    [HttpDelete("hs/{id}")]
    public async Task<IActionResult> DeleteHs(decimal id, CancellationToken ct)
        => await _svc.DeleteHsAsync(id, ct) == 0 ? NotFound() : NoContent();

    [HttpDelete("hs")]
    public async Task<IActionResult> PurgeHs(CancellationToken ct)
        => Ok(await _svc.DeleteAllHsAsync(ct));
}
