using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Api.Responses;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/v1/employee-planning")]
[Tags("EmployeePlanning")]
public class EmployeePlanningController : ControllerBase
{
    private readonly IEmployeePlanningService _planning;
    private readonly IPlanningImportService _imports;
    public EmployeePlanningController(IEmployeePlanningService planning, IPlanningImportService imports)
    {
        _planning = planning;
        _imports = imports;
    }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees(CancellationToken ct)
        => Ok(await _planning.ListEmployeesAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? cardId,
        CancellationToken ct)
        => Ok(await _planning.ListEmployeePlanningAsync(from, to, cardId, ct));

    /// <summary>Même liste que <c>GET /api/employee-planning</c>, paginée.</summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? cardId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken ct)
        => Ok(await _planning.ListEmployeePlanningPagedAsync(from, to, cardId, page, pageSize, search, ct));

    /// <summary>Salariés + nombre de jours planifiés (complet, pour l'export).</summary>
    [HttpGet("employees/stats")]
    public async Task<IActionResult> EmployeesStats(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
        => Ok(await _planning.ListEmployeeStatsAsync(from, to, ct));

    /// <summary>Salariés + nombre de jours planifiés, paginé.</summary>
    [HttpGet("employees/paged")]
    public async Task<IActionResult> EmployeesPaged(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken ct)
        => Ok(await _planning.ListEmployeeStatsPagedAsync(from, to, page, pageSize, search, ct));

    [HttpPost]
    public Task<IActionResult> Create([FromBody] List<EmployeePlanningDto> rows, CancellationToken ct)
        => SavePlanningAsync(rows, ct);

    [HttpPut]
    public Task<IActionResult> Update([FromBody] List<EmployeePlanningDto> rows, CancellationToken ct)
        => SavePlanningAsync(rows, ct);

    /// <summary>
    /// Une ligne incohérente (date manquante, shift inconnu, heures absentes) lève une
    /// <see cref="DomainException"/> que le middleware traduit en 400.
    /// </summary>
    private async Task<IActionResult> SavePlanningAsync(List<EmployeePlanningDto> rows, CancellationToken ct)
    {
        await _planning.SaveEmployeePlanningAsync(rows, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _planning.DeleteEmployeePlanningAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var rows = ExcelWorkbook.ReadFirstSheet(stream);
        var headers = rows.Count > 0 ? rows[0].Keys.ToList() : new List<string>();
        if (headers.Count == 0)
            return ApiErrors.Result(StatusCodes.Status400BadRequest, "Fichier Excel vide ou sans en-têtes.");
        return Ok(await _imports.ImportExcelAsync(rows, headers, ct));
    }

    /// <summary>
    /// Génère le planning d'une semaine pour toute une catégorie.
    /// CSV « catégorie ; lundi ; (début ; fin) × 7 », « OFF » pour un jour non travaillé.
    /// </summary>
    [HttpPost("generate-from-category")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> GenerateFromCategory(IFormFile file, CancellationToken ct)
        => Ok(await _imports.GenerateFromCategoryCsvAsync(await ReadCsvAsync(file, ct), ct));

    /// <summary>
    /// Génère le planning d'une semaine pour un ou plusieurs salariés.
    /// CSV « matricule ; lundi ; (début ; pause ; fin) × 7 », « OFF » pour un jour non travaillé.
    /// </summary>
    [HttpPost("generate")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Generate(IFormFile file, CancellationToken ct)
        => Ok(await _imports.GenerateFromMatriculeCsvAsync(await ReadCsvAsync(file, ct), ct));

    /// <summary>Lecture d'un CSV point-virgule (ou virgule) en lignes de cellules nettoyées.</summary>
    private static async Task<IReadOnlyList<string[]>> ReadCsvAsync(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync(ct);
        return text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l =>
            {
                var sep = l.Contains(';') ? ';' : ',';
                return l.Split(sep).Select(p => p.Trim().Trim('"')).ToArray();
            })
            .ToList();
    }
}
