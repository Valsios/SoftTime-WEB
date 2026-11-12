using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Overtime;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/overtime")]
[Tags("Overtime")]
public class OvertimeController : ApiControllerBase
{
    private readonly IOvertimeExoImpoService _exoImpo;
    private readonly IOvertimeDetailService _detail;
    private readonly IOvertimePayrollSyncService _payrollSync;
    public OvertimeController(IOvertimeExoImpoService exoImpo, IOvertimeDetailService detail, IOvertimePayrollSyncService payrollSync)
    {
        _exoImpo = exoImpo;
        _detail = detail;
        _payrollSync = payrollSync;
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] OvertimeListQuery query, CancellationToken ct)
        => Ok(await _exoImpo.ListExoAsync(query.ToPeriodRequest(withSearch: false), ct));

    /// <summary>Même liste que <c>GET /api/overtime</c>, paginée (page bornée, total serveur).</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> ListPagedAsync([FromQuery] OvertimeListQuery query, CancellationToken ct)
        => Ok(await _exoImpo.ListExoPagedAsync(query.ToPeriodRequest(withSearch: true), query.Page, query.PageSize, ct));

    [RequirePermission(Droit.Processing, "PROC:OVERTIME")]
    [HttpPost("calculate")]
    public async Task<IActionResult> CalculateAsync([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _exoImpo.CalculateExoImpoAsync(request, ct));

    [RequirePermission(Droit.Processing, "PROC:OVERTIME")]
    [HttpPost("purge")]
    public async Task<IActionResult> PurgeAsync([FromBody] PeriodRequest request, CancellationToken ct)
    {
        await _detail.PurgeHsAsync(request, ct);
        return NoContent();
    }

    [RequirePermission(Droit.Processing, "PROC:OVERTIME")]
    [HttpPost("sync-sage")]
    public async Task<IActionResult> SyncSageAsync([FromBody] PeriodRequest request, CancellationToken ct)
    {
        await _payrollSync.SyncSageAsync(request, ct);
        return NoContent();
    }

    [RequirePermission(Droit.Processing, "PROC:OVERTIME")]
    [HttpPost("sync-payroll")]
    public async Task<IActionResult> SyncPayrollAsync([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _payrollSync.SyncPayrollAsync(request, ct));

    /// <summary>Détail du calcul des heures supplémentaires d'un salarié (jours, semaines, EXO/IMPO).</summary>
    [HttpGet("detail/{matricule}")]
    public async Task<IActionResult> DetailAsync(string matricule, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Ok(await _detail.DetailAsync(matricule, from, to, ct));
}
