using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/field-roles")]
[Tags("FieldRoles")]
public class FieldRolesController : ControllerBase
{
    private readonly CatalogService _svc;
    public FieldRolesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? systemType, CancellationToken ct)
        => Ok(await _svc.GetFieldRolesAsync(systemType, ct));
}
