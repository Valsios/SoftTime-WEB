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

    /// <summary>Même liste que <c>GET /api/canteen</c>, paginée.</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> ListPaged(
        [FromQuery] PeriodRequest request, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCanteenPagedAsync(request, page, pageSize, ct));

    /// <summary>Anomalies du calcul cantine : pointage contredisant une absence complète.</summary>
    [HttpGet("anomalies")]
    public async Task<IActionResult> Anomalies([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ListCanteenAnomaliesAsync(request, ct));

    /// <summary>Anomalies cantine paginées (calcul complet puis découpage de la page).</summary>
    [HttpGet("anomalies/paged")]
    public async Task<IActionResult> AnomaliesPaged(
        [FromQuery] PeriodRequest request, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCanteenAnomaliesPagedAsync(request, page, pageSize, ct));

    [HttpPost("compute")]
    public async Task<IActionResult> Compute([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.ComputeCanteenAsync(request, ct));

    /// <summary>Pousse les totaux cantine de la période dans le cumul SAGE (T_CUMSAL).</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.SyncCanteenAsync(request, ct));

    /// <summary>Purge les cantines calculées sur la période.</summary>
    [HttpDelete]
    public async Task<IActionResult> Purge([FromQuery] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.PurgeCanteenAsync(request, ct));
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

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[Route("api/audit")]
[Tags("Audit")]
public class AuditController : ControllerBase
{
    private readonly AuditService _svc;
    public AuditController(AuditService svc) => _svc = svc;

    [HttpGet("types")]
    public IActionResult GetTypes() => Ok(_svc.ListAllowedTypes());

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct) => Ok(await _svc.ListConfigAsync(ct));

    /// <summary>Configuration d'audit paginée.</summary>
    [HttpGet("config/paged")]
    public async Task<IActionResult> GetConfigPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListConfigPagedAsync(page, pageSize, search, ct));

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

    /// <summary>Journal d'audit paginé (count + OFFSET/FETCH SQL sur le filtre complet).</summary>
    [HttpGet("events/paged")]
    public async Task<IActionResult> GetEventsPaged(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? table,
        [FromQuery] string? action,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => Ok(await _svc.ListEventsPagedAsync(from, to, table, action, search, page, pageSize, ct));

    [HttpGet("schema")]
    public async Task<IActionResult> GetSchema(CancellationToken ct)
        => Ok(await _svc.ListDiscoveredSchemaAsync(ct));
}
