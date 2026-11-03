using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
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
    private readonly IReportService _svc;
    public ReportsController(IReportService svc) => _svc = svc;

    /// <summary>
    /// Version paginée des 6 états (<c>pointage</c>, <c>absences</c>, <c>retards</c>,
    /// <c>hs-recap</c>, <c>heures-semaine</c>, <c>heures-dimanche</c>) ; 400 pour un autre type.
    /// </summary>
    [HttpGet("{type}/paged")]
    public async Task<IActionResult> Paged(
        string type,
        [FromQuery] ReportFilter filter,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => Ok(await _svc.PagedAsync(type, filter, page, pageSize, ct));

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
    private readonly IExtraService _svc;
    public CanteenController(IExtraService svc) => _svc = svc;

    /// <summary>Même liste que <c>GET /api/canteen</c>, paginée.</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> ListPaged(
        [FromQuery] PeriodRequest request, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCanteenPagedAsync(request, page, pageSize, ct));

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
    private readonly IExtraService _svc;
    public TravelController(IExtraService svc) => _svc = svc;

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
    private readonly IAuditService _svc;
    private readonly ICurrentUser _user;

    public AuditController(IAuditService svc, ICurrentUser user)
    {
        _svc = svc;
        _user = user;
    }

    [HttpGet("types")]
    public IActionResult GetTypes() => Ok(_svc.ListAllowedTypes());

    /// <summary>Configuration d'audit paginée.</summary>
    [HttpGet("config/paged")]
    public async Task<IActionResult> GetConfigPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListConfigPagedAsync(page, pageSize, search, ct));

    /// <summary>Configuration des déclencheurs d'audit : réservée aux administrateurs (filtre et contrôle en code).</summary>
    [RequireAdmin]
    [HttpPost("config")]
    public async Task<IActionResult> CreateConfig([FromBody] AuditConfigDto dto, CancellationToken ct)
    {
        AdminAccess.Require(_user);
        await _svc.SaveConfigAsync(dto with { Id = 0 }, ct);
        return NoContent();
    }

    [RequireAdmin]
    [HttpPut("config/{id:int}")]
    public async Task<IActionResult> UpdateConfig(int id, [FromBody] AuditConfigDto dto, CancellationToken ct)
    {
        AdminAccess.Require(_user);
        await _svc.SaveConfigAsync(dto with { Id = id }, ct);
        return NoContent();
    }

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
