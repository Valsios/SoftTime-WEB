using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/categories")]
[Tags("Categories")]
public class CategoriesController : ApiControllerBase
{
    private readonly ICategoryService _svc;
    public CategoriesController(ICategoryService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListCategoriesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCategoriesPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCategoryAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCategoryAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct)
    {
        await _svc.DeleteCategoryAsync(id, ct);
        return NoContent();
    }
}
