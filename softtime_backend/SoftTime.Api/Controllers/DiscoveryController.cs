using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;
[ApiController]
[Authorize]
[RequireDroit(Droit.Databases)]
[Route("api/v1/discovery")]
[Tags("Discovery")]
public class DiscoveryController : ApiControllerBase
{
    private readonly IDiscoveryService _svc;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;
    public DiscoveryController(IDiscoveryService svc, IDatabaseConfigurationAuthorizer configAuth)
    {
        _svc = svc;
        _configAuth = configAuth;
    }
    [HttpGet("servers")]
    public async Task<IActionResult> ServersAsync(CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverServersAsync(ct));
    }
    [HttpPost("databases")]
    public async Task<IActionResult> DatabasesAsync([FromBody] DiscoverDatabasesDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverDatabasesAsync(dto, ct));
    }
    [HttpPost("tables")]
    public async Task<IActionResult> TablesAsync([FromBody] DiscoverTablesDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverTablesAsync(dto, ct));
    }
    [HttpPost("columns")]
    public async Task<IActionResult> ColumnsAsync([FromBody] DiscoverColumnsDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(null, ct);
        return Ok(await _svc.DiscoverColumnsAsync(dto, ct));
    }
}
