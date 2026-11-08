using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/field-roles")]
[Tags("FieldRoles")]
public class FieldRolesController : ApiControllerBase
{
    private readonly IFieldRoleService _svc;
    public FieldRolesController(IFieldRoleService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync([FromQuery] string? systemType, CancellationToken ct)
        => Ok(await _svc.GetFieldRolesAsync(systemType, ct));
}
