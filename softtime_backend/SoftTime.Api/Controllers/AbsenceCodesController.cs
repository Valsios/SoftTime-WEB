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
[HasPermission(Permissions.AbsenceCodes.Read)]
[Route("api/v1/absence-codes")]
[Tags("AbsenceCodes")]
public class AbsenceCodesController : ApiControllerBase
{
    private readonly IAbsenceCodeService _svc;
    public AbsenceCodesController(IAbsenceCodeService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListAbsenceCodesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListAbsenceCodesPagedAsync(page, pageSize, search, ct));

    [HasPermission(Permissions.AbsenceCodes.Create)]
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] AbsenceCodeDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAbsenceCodeAsync(dto with { Id = 0 }, ct));

    [HasPermission(Permissions.AbsenceCodes.Update)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] AbsenceCodeDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAbsenceCodeAsync(dto with { Id = id }, ct));

    [HasPermission(Permissions.AbsenceCodes.Create)]
    [HttpPost("sync-sage")]
    public async Task<IActionResult> SyncSageAsync(CancellationToken ct)
    {
        await _svc.SyncAbsenceCodesFromSageAsync(ct);
        return NoContent();
    }
}
