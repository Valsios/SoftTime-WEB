using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.Abstractions.Excel;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/shifts")]
[Tags("Shifts")]
public class ShiftsController : ApiControllerBase
{
    private readonly IShiftService _svc;
    private readonly IExcelWorkbook _excel;
    public ShiftsController(IShiftService svc, IExcelWorkbook excel)
    {
        _svc = svc;
        _excel = excel;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListShiftsAsync(ct));

    /// <summary>Même catalogue que <c>GET /api/shifts</c>, paginé (page bornée, total serveur).</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListShiftsPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] ShiftDto dto, CancellationToken ct)
        => Ok(await _svc.SaveShiftAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(decimal id, [FromBody] ShiftDto dto, CancellationToken ct)
        => Ok(await _svc.SaveShiftAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(decimal id, CancellationToken ct)
    {
        await _svc.DeleteShiftAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcelAsync(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportShiftsExcelAsync(_excel.ReadFirstSheet(s), ct));
    }
}
