using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/shifts")]
[Tags("Shifts")]
public class ShiftsController : ControllerBase
{
    private readonly PlanningService _svc;
    public ShiftsController(PlanningService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListShiftsAsync(ct));

    /// <summary>Même catalogue que <c>GET /api/shifts</c>, paginé (page bornée, total serveur).</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListShiftsPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShiftDto dto, CancellationToken ct)
        => Ok(await _svc.SaveShiftAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] ShiftDto dto, CancellationToken ct)
        => Ok(await _svc.SaveShiftAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _svc.DeleteShiftAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportShiftsExcelAsync(ExcelWorkbook.ReadFirstSheet(s), ct));
    }
}
