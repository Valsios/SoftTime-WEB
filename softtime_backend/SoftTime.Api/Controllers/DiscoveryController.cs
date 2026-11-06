using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;
[ApiController]
[Authorize]
[RequireDroit(Droit.Databases)]
[Route("api/discovery")]
[Tags("Discovery")]
public class DiscoveryController : ControllerBase
{
    private readonly IDiscoveryService _svc;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;
    public DiscoveryController(IDiscoveryService svc, IDatabaseConfigurationAuthorizer configAuth)
    {
        _svc = svc;
        _configAuth = configAuth;
    }
    [HttpGet("servers")]
    public async Task<IActionResult> Servers(CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverServersAsync(ct));
    }
    [HttpPost("databases")]
    public async Task<IActionResult> Databases([FromBody] DiscoverDatabasesDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverDatabasesAsync(dto, ct));
    }
    [HttpPost("tables")]
    public async Task<IActionResult> Tables([FromBody] DiscoverTablesDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverTablesAsync(dto, ct));
    }
    [HttpPost("columns")]
    public async Task<IActionResult> Columns([FromBody] DiscoverColumnsDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverColumnsAsync(dto, ct));
    }
}
