using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/tolerances")]
[Tags("Tolerances")]
public class TolerancesController : ControllerBase
{
    private readonly CatalogService _svc;
    public TolerancesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? categoryId, CancellationToken ct)
        => Ok(await _svc.ListTolerancesAsync(categoryId, ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? categoryId, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListTolerancesPagedAsync(categoryId, page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteToleranceAsync(id, ct);
        return NoContent();
    }
}
