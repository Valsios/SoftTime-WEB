using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Excel;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.Abstractions.PunchProcessing;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/punches")]
[Tags("Punches")]
public class PunchesController : ApiControllerBase
{
    private readonly IPunchService _svc;
    private readonly IPunchImportService _imports;
    private readonly IPunchProcessingService _processing;
    private readonly IExcelWorkbook _excel;
    public PunchesController(IPunchService svc, IPunchImportService imports, IPunchProcessingService processing, IExcelWorkbook excel)
    {
        _svc = svc;
        _imports = imports;
        _processing = processing;
        _excel = excel;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListPunchesAsync(filter, ct));

    /// <summary>
    /// Même liste que <c>GET /api/punches</c>, mais paginée (page à partir de 1, taille plafonnée à 100).
    /// Variante additive : le contrat de l'endpoint non paginé est conservé pour l'export Excel du client.
    /// </summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync([FromQuery] PeriodRequest filter, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _svc.ListPunchesPagedAsync(filter, page, pageSize, ct));

    [RequirePermission(Droit.Processing, "PROC:PUNCHES")]
    [HttpPost("import-clock")]
    public async Task<IActionResult> ImportClockAsync([FromBody] ImportPunchesRequest request, CancellationToken ct)
        => Ok(await _imports.ImportFromClockAsync(request, ct));

    /// <summary>Pré-traitement : pointages → heures corrigées (T_HEURECORRIGER) + anomalies.</summary>
    [RequirePermission(Droit.Processing, "PROC:PUNCHES")]
    [HttpPost("preprocess")]
    public async Task<IActionResult> PreprocessAsync([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _processing.ProcessAsync(request, ct));

    /// <summary>Corrige un pointage importé (refusé si la semaine est validée).</summary>
    [RequirePermission(Droit.Processing, "PROC:PUNCHES")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(decimal id, [FromBody] PunchUpdateDto dto, CancellationToken ct)
        => Ok(await _svc.UpdatePunchAsync(id, dto, ct));

    /// <summary>Purge la période : pointages + corrections automatiques + anomalies (archivés).</summary>
    [RequirePermission(Droit.Processing, "PROC:PUNCHES")]
    [HttpDelete]
    public async Task<IActionResult> PurgeAsync([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.PurgePunchesAsync(filter, ct));

    [RequirePermission(Droit.Processing, "PROC:PUNCHES")]
    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcelAsync(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _imports.ImportPunchesExcelAsync(_excel.ReadFirstSheet(s), ct));
    }
}
