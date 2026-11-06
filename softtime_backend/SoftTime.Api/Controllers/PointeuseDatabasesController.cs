using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/pointeuse-databases")]
[Tags("PointeuseDatabases")]
public class PointeuseDatabasesController : ControllerBase
{
    private readonly IPointeuseDatabaseService _svc;
    private readonly IUserPermissionService _auth;
    private readonly ICurrentUser _user;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;

    public PointeuseDatabasesController(IPointeuseDatabaseService svc, IUserPermissionService auth, ICurrentUser user, IDatabaseConfigurationAuthorizer configAuth)
    {
        _svc = svc;
        _auth = auth;
        _user = user;
        _configAuth = configAuth;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _svc.ListPointeuseAsync(ct);
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                list = list.Where(s => !string.IsNullOrEmpty(s.NomBd) && p.GrantedPermissions.Contains($"POINTEUSE:{s.NomBd}:READ")).ToList();
            }
        }
        return Ok(list);
    }

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
    {
        IReadOnlyCollection<string>? allowed = null;
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
                allowed = DatabasePermissionNames.Read(p.GrantedPermissions, "POINTEUSE");
        }
        return Ok(await _svc.ListPointeusePagedAsync(allowed, page, pageSize, search, ct));
    }

    [HttpPost]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Create([FromBody] PointeuseDbDto dto, CancellationToken ct)
        => Ok(await _svc.SavePointeuseAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Update(int id, [FromBody] PointeuseDbDto dto, CancellationToken ct)
        => Ok(await _svc.SavePointeuseAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeletePointeuseAsync(id, ct);
        return NoContent();
    }

    [HttpPost("test-connection")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> TestConnection([FromBody] TestPointeuseConnectionDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(DatabaseKind.Pointeuse, ct);
        return Ok(await _svc.TestPointeuseConnectionAsync(dto, ct));
    }
}
