using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Excel;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/corrected-hours")]
[Tags("CorrectedHours")]
public class CorrectedHoursController : ApiControllerBase
{
    private readonly ICorrectedHoursService _svc;
    private readonly IExcelWorkbook _excel;
    public CorrectedHoursController(ICorrectedHoursService svc, IExcelWorkbook excel)
    {
        _svc = svc;
        _excel = excel;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListCorrectionsAsync(filter, ct));

    /// <summary>
    /// Même liste que <c>GET /api/corrected-hours</c>, mais paginée (page à partir de 1, taille plafonnée à 100).
    /// Variante additive : le contrat de l'endpoint non paginé est conservé pour l'export Excel du client.
    /// </summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync([FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListCorrectionsPagedAsync(filter, page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CorrectedHourDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCorrectionAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(decimal id, [FromBody] CorrectedHourDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCorrectionAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(decimal id, CancellationToken ct)
    {
        await _svc.DeleteCorrectionAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcelAsync(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportCorrectionsExcelAsync(_excel.ReadFirstSheet(s), ct));
    }
}
