using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[RequirePermission(Droit.Traceability, "TRACE:AUDIT")]
[Route("api/v1/audit")]
[Tags("Audit")]
public class AuditController : ApiControllerBase
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

    [HttpGet("config")]
    public async Task<IActionResult> GetConfigAsync(CancellationToken ct) => Ok(await _svc.ListConfigAsync(ct));

    /// <summary>Configuration d'audit paginée.</summary>
    [HttpGet("config/paged")]
    public async Task<IActionResult> GetConfigPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListConfigPagedAsync(page, pageSize, search, ct));

    /// <summary>Configuration des déclencheurs d'audit : réservée aux administrateurs (filtre et contrôle en code).</summary>
    [RequireAdmin]
    [HttpPost("config")]
    public async Task<IActionResult> CreateConfigAsync([FromBody] AuditConfigDto dto, CancellationToken ct)
    {
        AdminAccess.Require(_user);
        await _svc.SaveConfigAsync(dto with { Id = 0 }, ct);
        return NoContent();
    }

    [RequireAdmin]
    [HttpPut("config/{id:int}")]
    public async Task<IActionResult> UpdateConfigAsync(int id, [FromBody] AuditConfigDto dto, CancellationToken ct)
    {
        AdminAccess.Require(_user);
        await _svc.SaveConfigAsync(dto with { Id = id }, ct);
        return NoContent();
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEventsAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? table,
        [FromQuery] string? action,
        CancellationToken ct)
        => Ok(await _svc.ListEventsAsync(from, to, table, action, ct));

    /// <summary>Journal d'audit paginé (count + OFFSET/FETCH SQL sur le filtre complet).</summary>
    [HttpGet("events/paged")]
    public async Task<IActionResult> GetEventsPagedAsync([FromQuery] AuditEventsQuery query, CancellationToken ct)
        => Ok(await _svc.ListEventsPagedAsync(query, ct));

    [HttpGet("schema")]
    public async Task<IActionResult> GetSchemaAsync(CancellationToken ct)
        => Ok(await _svc.ListDiscoveredSchemaAsync(ct));
}
