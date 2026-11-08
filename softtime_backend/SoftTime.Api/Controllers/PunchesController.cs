using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/punches")]
[Tags("Punches")]
public class PunchesController : ControllerBase
{
    private readonly IPunchService _svc;
    private readonly IPunchImportService _imports;
    private readonly IPunchProcessingService _processing;
    public PunchesController(IPunchService svc, IPunchImportService imports, IPunchProcessingService processing)
    {
        _svc = svc;
        _imports = imports;
        _processing = processing;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListPunchesAsync(filter, ct));

    /// <summary>
    /// Même liste que <c>GET /api/punches</c>, mais paginée (page à partir de 1, taille plafonnée à 100).
    /// Variante additive : le contrat de l'endpoint non paginé est conservé pour l'export Excel du client.
    /// </summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged([FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListPunchesPagedAsync(filter, page, pageSize, ct));

    [HttpPost("import-clock")]
    public async Task<IActionResult> ImportClock([FromBody] ImportPunchesRequest request, CancellationToken ct)
        => Ok(await _imports.ImportFromClockAsync(request, ct));

    /// <summary>Pré-traitement : pointages → heures corrigées (T_HEURECORRIGER) + anomalies.</summary>
    [HttpPost("preprocess")]
    public async Task<IActionResult> Preprocess([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _processing.ProcessAsync(request, ct));

    /// <summary>Corrige un pointage importé (refusé si la semaine est validée).</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] PunchUpdateDto dto, CancellationToken ct)
        => Ok(await _svc.UpdatePunchAsync(id, dto, ct));

    /// <summary>Purge la période : pointages + corrections automatiques + anomalies (archivés).</summary>
    [HttpDelete]
    public async Task<IActionResult> Purge([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.PurgePunchesAsync(filter, ct));

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _imports.ImportPunchesExcelAsync(ExcelWorkbook.ReadFirstSheet(s), ct));
    }
}
