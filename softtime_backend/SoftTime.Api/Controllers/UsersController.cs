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
[Route("api/users")]
[Tags("Users")]
public class UsersController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly ICurrentUser _user;
    public UsersController(AuthService auth, ICurrentUser user)
    {
        _auth = auth;
        _user = user;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _auth.GetUsersAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _auth.GetUsersPagedAsync(page, pageSize, search, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(decimal id, CancellationToken ct) => Ok(await _auth.GetUserAsync(id, ct));

    [RequireAdmin]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserDto dto, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        return Ok(await _auth.SaveUserAsync(currentUserId, dto with { Id = 0 }, ct));
    }

    [RequireAdmin]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] UserDto dto, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        // Un changement de rôle non confirmé lève RoleChangeConfirmationRequiredException : 409 avec l'aperçu.
        return Ok(await _auth.SaveUserAsync(currentUserId, dto with { Id = id }, ct));
    }

    /// <summary>Aperçu d'un changement de rôle (lecture seule) : ajustements personnels qui seraient effacés.</summary>
    [RequireAdmin]
    [HttpPost("{id}/role-change-preview")]
    public async Task<IActionResult> RoleChangePreview(decimal id, [FromBody] RoleChangePreviewRequest request, CancellationToken ct)
        => Ok(await _auth.PreviewRoleChangeAsync(id, request.NewRoleId, ct));

    [RequireAdmin]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _auth.DeleteUserAsync(currentUserId, id, ct);
        return NoContent();
    }
}
