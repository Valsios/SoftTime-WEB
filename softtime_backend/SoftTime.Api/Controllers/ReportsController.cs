using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Api.Responses;
using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Reports)]
[RequirePermission(Droit.Reports, "REPORTS:REPORTS")]
[Route("api/v1/reports")]
[Tags("Reports")]
public class ReportsController : ApiControllerBase
{
    private readonly IReportService _svc;
    private readonly IPointageReportService _pointages;
    private readonly IAbsenceReportService _absences;
    private readonly IRetardReportService _retards;
    private readonly IHoursReportService _heures;

    public ReportsController(
        IReportService svc,
        IPointageReportService pointages,
        IAbsenceReportService absences,
        IRetardReportService retards,
        IHoursReportService heures)
    {
        _svc = svc;
        _pointages = pointages;
        _absences = absences;
        _retards = retards;
        _heures = heures;
    }

    /// <summary>Types exposés par l'endpoint paginé générique (liste blanche).</summary>
    private static readonly HashSet<string> PagedReportTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pointage", "absences", "retards", "hs-recap", "heures-semaine", "heures-dimanche"
    };

    [HttpGet("pointage")]
    public async Task<IActionResult> PointageAsync([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _pointages.PointageAsync(filter, ct));

    [HttpGet("absences")]
    public async Task<IActionResult> AbsencesAsync([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _absences.AbsencesAsync(filter, ct));

    [HttpGet("retards")]
    public async Task<IActionResult> RetardsAsync([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _retards.RetardsAsync(filter, ct));

    [HttpGet("hs-recap")]
    public async Task<IActionResult> HsRecapAsync([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _heures.HsRecapAsync(filter, ct));

    [HttpGet("heures-semaine")]
    public async Task<IActionResult> HeuresSemaineAsync([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _heures.HeuresParSemaineAsync(filter, ct));

    [HttpGet("heures-dimanche")]
    public async Task<IActionResult> HeuresDimancheAsync([FromQuery] ReportFilter filter, CancellationToken ct)
        => Ok(await _heures.HeuresDimancheAsync(filter, ct));

    /// <summary>
    /// Version paginée des 6 états (<c>pointage</c>, <c>absences</c>, <c>retards</c>,
    /// <c>hs-recap</c>, <c>heures-semaine</c>, <c>heures-dimanche</c>).
    /// </summary>
    [HttpGet("{type}/paged")]
    public async Task<IActionResult> PagedAsync(
        string type,
        [FromQuery] ReportFilter filter,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var normalized = type.Trim().ToLowerInvariant();
        if (!PagedReportTypes.Contains(normalized))
            return ApiErrors.Result(StatusCodes.Status400BadRequest, $"Type de rapport inconnu : {type}.");
        return Ok(await _svc.PagedAsync(normalized, filter, page, pageSize, ct));
    }

    [HttpGet("leave")]
    public async Task<IActionResult> LeaveAsync([FromQuery] string matricule, [FromQuery] DateTime date, CancellationToken ct)
        => Ok(await _svc.GetLeaveAsync(matricule, date, ct));
}
