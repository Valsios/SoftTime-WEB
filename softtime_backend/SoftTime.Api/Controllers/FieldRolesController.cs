using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/field-roles")]
[Tags("FieldRoles")]
public class FieldRolesController : ControllerBase
{
    private readonly ICatalogService _svc;
    public FieldRolesController(ICatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? systemType, CancellationToken ct)
        => Ok(await _svc.GetFieldRolesAsync(systemType, ct));
}
