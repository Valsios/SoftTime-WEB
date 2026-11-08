using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/code-constantes")]
[Tags("CodeConstantes")]
public class CodeConstantesController : ControllerBase
{
    private readonly ICodeConstanteService _svc;
    public CodeConstantesController(ICodeConstanteService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCodeConstantesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCodeConstantesPagedAsync(page, pageSize, search, ct));

    [HttpGet("sage-options")]
    public async Task<IActionResult> SageOptions(CancellationToken ct) => Ok(await _svc.ListSageConstantOptionsAsync(ct));

    [HttpGet("autre-tables")]
    public async Task<IActionResult> OtherTables(CancellationToken ct) => Ok(await _svc.ListOtherCodeConstantTablesAsync(ct));

    [HttpGet("autre-values")]
    public async Task<IActionResult> OtherValues([FromQuery] string table, CancellationToken ct)
        => Ok(await _svc.ListOtherCodeConstantValuesAsync(table, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CodeConstanteDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCodeConstanteAsync(dto with { Id = id }, ct));
}
