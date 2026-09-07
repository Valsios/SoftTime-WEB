using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Reports)]
[Route("api/reports")]
[Tags("Reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _svc;
    public ReportsController(ReportService svc) => _svc = svc;

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

    [HttpGet("leave")]
    public async Task<IActionResult> Leave([FromQuery] string matricule, [FromQuery] DateTime date, CancellationToken ct)
        => Ok(await _svc.GetLeaveAsync(matricule, date, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Other)]
[Route("api/canteen")]
[Tags("Canteen")]
public class CanteenController : ControllerBase
{
    private readonly ExtraService _svc;
    public CanteenController(ExtraService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ListCanteenAsync(request, ct));

    [HttpPost("compute")]
    public async Task<IActionResult> Compute([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ComputeCanteenAsync(request, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Other)]
[Route("api/travel-expenses")]
[Tags("TravelExpenses")]
public class TravelController : ControllerBase
{
    private readonly ExtraService _svc;
    public TravelController(ExtraService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ListTravelAsync(request, ct));

    [HttpPost("compute")]
    public async Task<IActionResult> Compute([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ComputeTravelAsync(request, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[Route("api/audit")]
[Tags("Audit")]
public class AuditController : ControllerBase
{
    private readonly AuditService _svc;
    public AuditController(AuditService svc) => _svc = svc;

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct) => Ok(await _svc.ListConfigAsync(ct));

    [HttpPost("config")]
    public async Task<IActionResult> CreateConfig([FromBody] AuditConfigDto dto, CancellationToken ct)
    {
        await _svc.SaveConfigAsync(dto with { Id = 0 }, ct);
        return NoContent();
    }

    [HttpPut("config/{id:int}")]
    public async Task<IActionResult> UpdateConfig(int id, [FromBody] AuditConfigDto dto, CancellationToken ct)
    {
        await _svc.SaveConfigAsync(dto with { Id = id }, ct);
        return NoContent();
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? table,
        [FromQuery] string? action,
        CancellationToken ct)
        => Ok(await _svc.ListEventsAsync(from, to, table, action, ct));

    [HttpGet("schema")]
    public async Task<IActionResult> GetSchema(CancellationToken ct)
        => Ok(await _svc.ListDiscoveredSchemaAsync(ct));
}
