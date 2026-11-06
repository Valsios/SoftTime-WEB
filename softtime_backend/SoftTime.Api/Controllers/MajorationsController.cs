using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/majorations")]
[Tags("Majorations")]
public class MajorationsController : ControllerBase
{
    private readonly CatalogService _svc;
    public MajorationsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListMajorationsAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListMajorationsPagedAsync(page, pageSize, search, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MajorationDto dto, CancellationToken ct)
        => Ok(await _svc.SaveMajorationAsync(dto with { Id = id }, ct));
}
