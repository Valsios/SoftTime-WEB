using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/categories")]
[Tags("Categories")]
public class CategoriesController : ControllerBase
{
    private readonly CatalogService _svc;
    public CategoriesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCategoriesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCategoriesPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCategoryAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCategoryAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteCategoryAsync(id, ct);
        return NoContent();
    }
}
