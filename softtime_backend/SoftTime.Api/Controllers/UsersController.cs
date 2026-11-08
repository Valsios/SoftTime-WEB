using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/users")]
[Tags("Users")]
public class UsersController : ApiControllerBase
{
    private readonly IUserDirectoryService _directory;
    private readonly IUserAdministrationService _administration;
    private readonly IUserDeletionService _deletion;
    private readonly ICurrentUser _user;
    public UsersController(
        IUserDirectoryService directory,
        IUserAdministrationService administration,
        IUserDeletionService deletion,
        ICurrentUser user)
    {
        _directory = directory;
        _administration = administration;
        _deletion = deletion;
        _user = user;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _directory.GetUsersAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _directory.GetUsersPagedAsync(page, pageSize, search, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(decimal id, CancellationToken ct) => Ok(await _directory.GetUserAsync(id, ct));

    [RequireAdmin]
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] UserDto dto, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        return Ok(await _administration.SaveUserAsync(currentUserId, dto with { Id = 0 }, ct));
    }

    [RequireAdmin]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(decimal id, [FromBody] UserDto dto, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        // Un changement de rôle non confirmé lève RoleChangeConfirmationRequiredException : 409 avec l'aperçu.
        return Ok(await _administration.SaveUserAsync(currentUserId, dto with { Id = id }, ct));
    }

    /// <summary>Aperçu d'un changement de rôle (lecture seule) : ajustements personnels qui seraient effacés.</summary>
    [RequireAdmin]
    [HttpPost("{id}/role-change-preview")]
    public async Task<IActionResult> RoleChangePreviewAsync(decimal id, [FromBody] RoleChangePreviewRequest request, CancellationToken ct)
        => Ok(await _administration.PreviewRoleChangeAsync(id, request.NewRoleId, ct));

    [RequireAdmin]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(decimal id, CancellationToken ct)
    {
        if (_user.UserId is not decimal currentUserId)
            return Unauthorized();
        await _deletion.DeleteUserAsync(currentUserId, id, ct);
        return NoContent();
    }
}
