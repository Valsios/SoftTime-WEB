using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/holidays")]
[Tags("Holidays")]
public class HolidaysController : ControllerBase
{
    private readonly ICatalogService _svc;
    public HolidaysController(ICatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListHolidaysAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListHolidaysPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HolidayDto dto, CancellationToken ct)
        => Ok(await _svc.SaveHolidayAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] HolidayDto dto, CancellationToken ct)
        => Ok(await _svc.SaveHolidayAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteHolidayAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-sage")]
    public async Task<IActionResult> ImportSage(CancellationToken ct)
    {
        await _svc.ImportSageHolidaysAsync(ct);
        return NoContent();
    }
}
