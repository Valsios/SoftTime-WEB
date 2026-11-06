using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Reports)]
[Route("api/reports")]
[Tags("Reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _svc;
    public ReportsController(IReportService svc) => _svc = svc;

    /// <summary>Types exposés par l'endpoint paginé générique (liste blanche).</summary>
    private static readonly HashSet<string> PagedReportTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pointage", "absences", "retards", "hs-recap", "heures-semaine", "heures-dimanche"
    };

    [HttpGet("pointage")]
    public async Task<IActionResult> Pointage([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _svc.PointageAsync(filter, ct));

    [HttpGet("absences")]
    public async Task<IActionResult> Absences([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _svc.AbsencesAsync(filter, ct));

    [HttpGet("retards")]
    public async Task<IActionResult> Retards([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _svc.RetardsAsync(filter, ct));

    [HttpGet("hs-recap")]
    public async Task<IActionResult> HsRecap([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _svc.HsRecapAsync(filter, ct));

    [HttpGet("heures-semaine")]
    public async Task<IActionResult> HeuresSemaine([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _svc.HeuresParSemaineAsync(filter, ct));

    [HttpGet("heures-dimanche")]
    public async Task<IActionResult> HeuresDimanche([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _svc.HeuresDimancheAsync(filter, ct));

    /// <summary>
    /// Version paginée des 6 états (<c>pointage</c>, <c>absences</c>, <c>retards</c>,
    /// <c>hs-recap</c>, <c>heures-semaine</c>, <c>heures-dimanche</c>).
    /// </summary>
    [HttpGet("{type}/paged")]
    public async Task<IActionResult> Paged(
        string type,
        [FromQuery] ReportFilter filter,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var normalized = type.Trim().ToLowerInvariant();
        if (!PagedReportTypes.Contains(normalized))
            return BadRequest($"Type de rapport inconnu : {type}.");
        return Ok(await _svc.PagedAsync(normalized, filter, page, pageSize, ct));
    }

    [HttpGet("leave")]
    public async Task<IActionResult> Leave([FromQuery] string matricule, [FromQuery] DateTime date, CancellationToken ct)
        => Ok(await _svc.GetLeaveAsync(matricule, date, ct));
}
