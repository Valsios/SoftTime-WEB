using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/privileges")]
[Tags("Privileges")]
public class PrivilegesController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly ICurrentUser _user;
    public PrivilegesController(AuthService auth, ICurrentUser user)
    {
        _auth = auth;
        _user = user;
    }

    [HttpGet("droits")]
    public async Task<IActionResult> GetDroits(CancellationToken ct) => Ok(await _auth.GetDroitsAsync(ct));

    [HttpGet]
    public async Task<IActionResult> GetByRole([FromQuery] decimal? roleId, CancellationToken ct)
        => Ok(await _auth.GetPrivilegesAsync(roleId, ct));

    [RequireAdmin]
    [HttpPut("{roleId}")]
    public async Task<IActionResult> Replace(decimal roleId, [FromBody] List<decimal> droits, CancellationToken ct)
    {
        await _auth.SetPrivilegesAsync(roleId, droits, ct);
        return NoContent();
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserPermissions(decimal userId, CancellationToken ct)
    {
        // GetUserDetailedPermissionsAsync sert aussi en interne (connexion, listes de bases) : le contrôle est donc ici.
        await _auth.EnsureCanReadAsync(AuthService.ReadPrivileges, ct);
        return Ok(await _auth.GetUserDetailedPermissionsAsync(userId, ct));
    }

    [RequireAdmin]
    [HttpPut("user/{userId}")]
    public async Task<IActionResult> SaveUserPermissions(decimal userId, [FromBody] SaveUserDetailedPermissionsRequest request, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _auth.SaveUserDetailedPermissionsAsync(currentUserId, userId, request.GrantedPermissions, ct);
        return NoContent();
    }

    [HttpGet("role/{roleId}")]
    public async Task<IActionResult> GetRolePermissions(decimal roleId, CancellationToken ct)
        => Ok(await _auth.GetRoleDetailedPermissionsAsync(roleId, ct));

    [HttpPut("role/{roleId}")]
    public async Task<IActionResult> SaveRolePermissions(decimal roleId, [FromBody] SaveRoleDetailedPermissionsRequest request, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId, roleId, request.GrantedPermissions, ct);
        return NoContent();
    }

    [RequireAdmin]
    [HttpPost("user/{userId}/reset")]
    public async Task<IActionResult> ResetUserPermissions(decimal userId, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _auth.ResetUserDetailedPermissionsAsync(currentUserId, userId, ct);
        return NoContent();
    }
}
