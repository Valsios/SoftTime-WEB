using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/roles")]
[Tags("Roles")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _auth;
    public RolesController(IRoleService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _auth.GetRolesAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _auth.GetRolesPagedAsync(page, pageSize, search, ct));

    [RequireAdmin]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RoleDto dto, CancellationToken ct)
        => Ok(await _auth.SaveRoleAsync(dto with { Id = 0 }, ct));

    [RequireAdmin]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] RoleDto dto, CancellationToken ct)
        => Ok(await _auth.SaveRoleAsync(dto with { Id = id }, ct));

    [RequireAdmin]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _auth.DeleteRoleAsync(id, ct);
        return NoContent();
    }
}
