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
[Route("api/corrected-hours")]
[Tags("CorrectedHours")]
public class CorrectedHoursController : ControllerBase
{
    private readonly TimekeepingService _svc;
    public CorrectedHoursController(TimekeepingService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListCorrectionsAsync(filter, ct));

    /// <summary>
    /// Même liste que <c>GET /api/corrected-hours</c>, mais paginée (page à partir de 1, taille plafonnée à 100).
    /// Variante additive : le contrat de l'endpoint non paginé est conservé pour l'export Excel du client.
    /// </summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged([FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCorrectionsPagedAsync(filter, page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CorrectedHourDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCorrectionAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] CorrectedHourDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCorrectionAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _svc.DeleteCorrectionAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportCorrectionsExcelAsync(ExcelWorkbook.ReadFirstSheet(s), ct));
    }
}
