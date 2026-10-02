using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
namespace SoftTime.Api.Controllers;
[ApiController]
[Authorize]
[Route("api/discovery")]
[Tags("Discovery")]
public class DiscoveryController : ControllerBase
{
    private readonly CatalogService _svc;
    public DiscoveryController(CatalogService svc) => _svc = svc;
    [HttpGet("servers")]
    public async Task<IActionResult> Servers(CancellationToken ct)
        => Ok(await _svc.DiscoverServersAsync(ct));
    [HttpPost("databases")]
    public async Task<IActionResult> Databases([FromBody] DiscoverDatabasesDto dto, CancellationToken ct)
        => Ok(await _svc.DiscoverDatabasesAsync(dto, ct));
    [HttpPost("tables")]
    public async Task<IActionResult> Tables([FromBody] DiscoverTablesDto dto, CancellationToken ct)
        => Ok(await _svc.DiscoverTablesAsync(dto, ct));
    [HttpPost("columns")]
    public async Task<IActionResult> Columns([FromBody] DiscoverColumnsDto dto, CancellationToken ct)
        => Ok(await _svc.DiscoverColumnsAsync(dto, ct));
}