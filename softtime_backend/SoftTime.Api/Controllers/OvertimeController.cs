using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/overtime")]
[Tags("Overtime")]
public class OvertimeController : ControllerBase
{
    private readonly OvertimeService _svc;
    public OvertimeController(OvertimeService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string? matriculeFrom,
        [FromQuery] string? matriculeTo,
        [FromQuery] string? departement,
        CancellationToken ct)
        => Ok(await _svc.ListExoAsync(new PeriodRequest(matriculeFrom, matriculeTo, from, to, departement), ct));

    /// <summary>Même liste que <c>GET /api/overtime</c>, paginée (page bornée, total serveur).</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> ListPaged(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string? matriculeFrom,
        [FromQuery] string? matriculeTo,
        [FromQuery] string? departement,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken ct)
        => Ok(await _svc.ListExoPagedAsync(
            new PeriodRequest(matriculeFrom, matriculeTo, from, to, departement, search),
            page,
            pageSize,
            ct));

    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.CalculateExoImpoAsync(request, ct));

    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromBody] PeriodRequest request, CancellationToken ct)
    {
        await _svc.PurgeHsAsync(request, ct);
        return NoContent();
    }

    [HttpPost("sync-sage")]
    public async Task<IActionResult> SyncSage([FromBody] PeriodRequest request, CancellationToken ct)
    {
        await _svc.SyncSageAsync(request, ct);
        return NoContent();
    }

    [HttpPost("sync-payroll")]
    public async Task<IActionResult> SyncPayroll([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.SyncPayrollAsync(request, ct));

    /// <summary>Détail du calcul des heures supplémentaires d'un salarié (jours, semaines, EXO/IMPO).</summary>
    [HttpGet("detail/{matricule}")]
    public async Task<IActionResult> Detail(string matricule, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Ok(await _svc.DetailAsync(matricule, from, to, ct));
}
