using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/absence-codes")]
[Tags("AbsenceCodes")]
public class AbsenceCodesController : ControllerBase
{
    private readonly ICatalogService _svc;
    public AbsenceCodesController(ICatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListAbsenceCodesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListAbsenceCodesPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenceCodeDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAbsenceCodeAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenceCodeDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAbsenceCodeAsync(dto with { Id = id }, ct));

    [HttpPost("sync-sage")]
    public async Task<IActionResult> SyncSage(CancellationToken ct)
    {
        await _svc.SyncAbsenceCodesFromSageAsync(ct);
        return NoContent();
    }
}
