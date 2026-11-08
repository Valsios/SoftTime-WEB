using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/affectations")]
[Tags("Affectations")]
public class AffectationsController : ApiControllerBase
{
    private readonly IAffectationService _svc;
    private readonly IAffectationImportService _imports;

    public AffectationsController(IAffectationService svc, IAffectationImportService imports)
    {
        _svc = svc;
        _imports = imports;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync([FromQuery] int? categoryId, CancellationToken ct)
        => Ok(await _svc.ListAffectationsAsync(categoryId, ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync([FromQuery] AffectationsQuery query, CancellationToken ct)
        => Ok(await _svc.ListAffectationsPagedAsync(query, ct));

    [HttpGet("options")]
    public async Task<IActionResult> GetOptionsAsync(CancellationToken ct)
        => Ok(await _svc.GetAffectationOptionsAsync(ct));

    [HttpGet("all")]
    public async Task<IActionResult> GetAllRowsAsync(
        [FromQuery] int? categoryId,
        [FromQuery] string? departement,
        [FromQuery] string? service,
        CancellationToken ct)
        => Ok(await _svc.ListAllAffectationsAsync(categoryId, departement, service, ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] AffectationDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAffectationAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] AffectationDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAffectationAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct)
    {
        await _svc.DeleteAffectationAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcelAsync(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _imports.ImportAffectationsExcelAsync(ExcelWorkbook.ReadFirstSheet(s), ct));
    }
}
