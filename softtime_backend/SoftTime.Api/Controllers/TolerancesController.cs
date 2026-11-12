using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Security;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[HasPermission(Permissions.Tolerances.Read)]
[Route("api/v1/tolerances")]
[Tags("Tolerances")]
public class TolerancesController : ApiControllerBase
{
    private readonly IToleranceService _svc;
    public TolerancesController(IToleranceService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync([FromQuery] int? categoryId, CancellationToken ct)
        => Ok(await _svc.ListTolerancesAsync(categoryId, ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? categoryId, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListTolerancesPagedAsync(categoryId, page, pageSize, search, ct));

    [HasPermission(Permissions.Tolerances.Create)]
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = 0 }, ct));

    [HasPermission(Permissions.Tolerances.Update)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = id }, ct));

    [HasPermission(Permissions.Tolerances.Update)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct)
    {
        await _svc.DeleteToleranceAsync(id, ct);
        return NoContent();
    }
}
