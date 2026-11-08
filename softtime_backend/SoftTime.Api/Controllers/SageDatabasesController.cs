using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sage-databases")]
[Tags("SageDatabases")]
public class SageDatabasesController : ControllerBase
{
    private readonly ISageDatabaseService _svc;
    private readonly IDepartementServiceLookup _departements;
    private readonly IUserPermissionService _auth;
    private readonly ICurrentUser _user;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;

    public SageDatabasesController(
        ISageDatabaseService svc,
        IDepartementServiceLookup departements,
        IUserPermissionService auth,
        ICurrentUser user,
        IDatabaseConfigurationAuthorizer configAuth)
    {
        _svc = svc;
        _departements = departements;
        _auth = auth;
        _user = user;
        _configAuth = configAuth;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _svc.ListSageAsync(ct);
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                list = list.Where(s => !string.IsNullOrEmpty(s.NomBd) && p.GrantedPermissions.Contains($"SAGE:{s.NomBd}:READ")).ToList();
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
                allowed = DatabasePermissionNames.Read(p.GrantedPermissions, "SAGE");
        }
        return Ok(await _svc.ListSagePagedAsync(allowed, page, pageSize, search, ct));
    }

    [HttpPost]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Create([FromBody] SageDbDto dto, CancellationToken ct)
        => Ok(await _svc.SaveSageAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Update(int id, [FromBody] SageDbDto dto, CancellationToken ct)
        => Ok(await _svc.SaveSageAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteSageAsync(id, ct);
        return NoContent();
    }

    [HttpPost("test-connection")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> TestConnection([FromBody] TestSageConnectionDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(DatabaseKind.Sage, ct);
        return Ok(await _svc.TestSageConnectionAsync(dto, ct));
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(CancellationToken ct)
        => Ok(await _svc.ActivateSagePairAsync(ct));

    [HttpGet("lookup/{matricule}")]
    public async Task<IActionResult> Lookup(string matricule, CancellationToken ct)
        => Ok(await _departements.GetDepartementServiceAsync(matricule, ct));

    [HttpPost("lookup-batch")]
    public async Task<IActionResult> LookupBatch([FromBody] string[] matricules, CancellationToken ct)
        => Ok(await _departements.GetDepartementServiceBatchAsync(matricules ?? Array.Empty<string>(), ct));

    [HttpGet("departement-service-options")]
    public async Task<IActionResult> DepartementServiceOptions(CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceOptionsAsync(ct));
}
