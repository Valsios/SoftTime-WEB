using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Security;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[HasPermission(Permissions.CodeConstantes.Manage)]
[Route("api/v1/code-constantes")]
[Tags("CodeConstantes")]
public class CodeConstantesController : ApiControllerBase
{
    private readonly ICodeConstanteService _svc;
    public CodeConstantesController(ICodeConstanteService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListCodeConstantesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCodeConstantesPagedAsync(page, pageSize, search, ct));

    [HttpGet("sage-options")]
    public async Task<IActionResult> SageOptionsAsync(CancellationToken ct) => Ok(await _svc.ListSageConstantOptionsAsync(ct));

    [HttpGet("autre-tables")]
    public async Task<IActionResult> OtherTablesAsync(CancellationToken ct) => Ok(await _svc.ListOtherCodeConstantTablesAsync(ct));

    [HttpGet("autre-values")]
    public async Task<IActionResult> OtherValuesAsync([FromQuery] string table, CancellationToken ct)
        => Ok(await _svc.ListOtherCodeConstantValuesAsync(table, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] CodeConstanteDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCodeConstanteAsync(dto with { Id = id }, ct));
}
