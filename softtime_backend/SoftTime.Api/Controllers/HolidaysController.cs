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
[HasPermission(Permissions.Holidays.Read)]
[Route("api/v1/holidays")]
[Tags("Holidays")]
public class HolidaysController : ApiControllerBase
{
    private readonly IHolidayService _svc;
    public HolidaysController(IHolidayService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListHolidaysAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListHolidaysPagedAsync(page, pageSize, search, ct));

    [HasPermission(Permissions.Holidays.Create)]
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] HolidayDto dto, CancellationToken ct)
        => Ok(await _svc.SaveHolidayAsync(dto with { Id = 0 }, ct));

    [HasPermission(Permissions.Holidays.Update)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] HolidayDto dto, CancellationToken ct)
        => Ok(await _svc.SaveHolidayAsync(dto with { Id = id }, ct));

    [HasPermission(Permissions.Holidays.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct)
    {
        await _svc.DeleteHolidayAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Holidays.Create)]
    [HttpPost("import-sage")]
    public async Task<IActionResult> ImportSageAsync(CancellationToken ct)
    {
        await _svc.ImportSageHolidaysAsync(ct);
        return NoContent();
    }
}
