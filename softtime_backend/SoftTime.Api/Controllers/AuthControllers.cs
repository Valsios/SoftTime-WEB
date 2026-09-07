using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly ICurrentUser _user;
    public AuthController(AuthService auth, ICurrentUser user)
    {
        _auth = auth;
        _user = user;
    }

    /// <summary>POST — Authentification (LOGIN/PASSWORD T_RESPONSABLE). Retourne le JWT.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>GET — Profil courant + bases SAGE autorisées.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Me(CancellationToken ct)
    {
        if (_user.UserId is not decimal id)
            return Unauthorized();
        return Ok(await _auth.GetMeAsync(id, ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/users")]
[Tags("Users")]
public class UsersController : ControllerBase
{
    private readonly AuthService _auth;
    public UsersController(AuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _auth.GetUsersAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(decimal id, CancellationToken ct) => Ok(await _auth.GetUserAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserDto dto, CancellationToken ct)
        => Ok(await _auth.SaveUserAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] UserDto dto, CancellationToken ct)
        => Ok(await _auth.SaveUserAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _auth.DeleteUserAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/roles")]
[Tags("Roles")]
public class RolesController : ControllerBase
{
    private readonly AuthService _auth;
    public RolesController(AuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _auth.GetRolesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RoleDto dto, CancellationToken ct)
        => Ok(await _auth.SaveRoleAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] RoleDto dto, CancellationToken ct)
        => Ok(await _auth.SaveRoleAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _auth.DeleteRoleAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/privileges")]
[Tags("Privileges")]
public class PrivilegesController : ControllerBase
{
    private readonly AuthService _auth;
    public PrivilegesController(AuthService auth) => _auth = auth;

    [HttpGet("droits")]
    public async Task<IActionResult> GetDroits(CancellationToken ct) => Ok(await _auth.GetDroitsAsync(ct));

    [HttpGet]
    public async Task<IActionResult> GetByRole([FromQuery] decimal? roleId, CancellationToken ct)
        => Ok(await _auth.GetPrivilegesAsync(roleId, ct));

    [HttpPut("{roleId}")]
    public async Task<IActionResult> Replace(decimal roleId, [FromBody] List<decimal> droits, CancellationToken ct)
    {
        await _auth.SetPrivilegesAsync(roleId, droits, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/db-access")]
[Tags("DbAccess")]
public class DbAccessController : ControllerBase
{
    private readonly AuthService _auth;
    public DbAccessController(AuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] decimal? userId, CancellationToken ct)
        => Ok(await _auth.GetDbAccessAsync(userId, ct));

    [HttpPut("{userId}")]
    public async Task<IActionResult> Replace(decimal userId, [FromBody] List<int> sageDbIds, CancellationToken ct)
    {
        await _auth.SetDbAccessAsync(userId, sageDbIds, ct);
        return NoContent();
    }
}
