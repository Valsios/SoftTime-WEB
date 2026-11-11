using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/privileges")]
[Tags("Privileges")]
public class PrivilegesController : ApiControllerBase
{
    private readonly IPrivilegeService _privileges;
    private readonly IParametersAccessGuard _guard;
    private readonly IUserPermissionService _userPermissions;
    private readonly IRolePermissionService _rolePermissions;
    private readonly ICurrentUser _user;
    public PrivilegesController(
        IPrivilegeService privileges,
        IParametersAccessGuard guard,
        IUserPermissionService userPermissions,
        IRolePermissionService rolePermissions,
        ICurrentUser user)
    {
        _privileges = privileges;
        _guard = guard;
        _userPermissions = userPermissions;
        _rolePermissions = rolePermissions;
        _user = user;
    }

    [HasPermission(Permissions.Privileges.Read)]
    [HttpGet("catalog")]
    public IActionResult GetCatalog() => Ok(_privileges.GetCatalog());

    [HttpGet("droits")]
    public async Task<IActionResult> GetDroitsAsync(CancellationToken ct) => Ok(await _privileges.GetDroitsAsync(ct));

    [HttpGet]
    public async Task<IActionResult> GetByRoleAsync([FromQuery] decimal? roleId, CancellationToken ct)
        => Ok(await _privileges.GetPrivilegesAsync(roleId, ct));

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserPermissionsAsync(decimal userId, CancellationToken ct)
    {
        // GetUserDetailedPermissionsAsync sert aussi en interne (connexion, listes de bases) : le contrôle est donc ici.
        await _guard.EnsureCanReadAsync(ReadPermissions.Privileges, ct);
        return Ok(await _userPermissions.GetUserDetailedPermissionsAsync(userId, ct));
    }

    [RequireAdmin]
    [HttpPut("user/{userId}")]
    public async Task<IActionResult> SaveUserPermissionsAsync(decimal userId, [FromBody] SaveUserDetailedPermissionsRequest request, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _userPermissions.SaveUserDetailedPermissionsAsync(currentUserId, userId, request.GrantedPermissions, ct);
        return NoContent();
    }

    [HttpGet("role/{roleId}")]
    public async Task<IActionResult> GetRolePermissionsAsync(decimal roleId, CancellationToken ct)
        => Ok(await _rolePermissions.GetRoleDetailedPermissionsAsync(roleId, ct));

    [RequireAdmin]
    [HttpPut("role/{roleId}")]
    public async Task<IActionResult> SaveRolePermissionsAsync(decimal roleId, [FromBody] SaveRoleDetailedPermissionsRequest request, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _rolePermissions.SaveRoleDetailedPermissionsAsync(currentUserId, roleId, request.GrantedPermissions, ct);
        return NoContent();
    }

    [RequireAdmin]
    [HttpPost("user/{userId}/reset")]
    public async Task<IActionResult> ResetUserPermissionsAsync(decimal userId, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _userPermissions.ResetUserDetailedPermissionsAsync(currentUserId, userId, ct);
        return NoContent();
    }
}
