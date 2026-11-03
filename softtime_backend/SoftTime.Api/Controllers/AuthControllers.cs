using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SoftTime.Api.Authorization;
using SoftTime.Api.Security;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
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

    /// <summary>
    /// POST — Authentification (LOGIN/PASSWORD T_RESPONSABLE). Retourne le jeton d'accès (courte durée) et le jeton
    /// de rafraîchissement. Compte verrouillé après plusieurs échecs (403), requêtes limitées par adresse IP (429).
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>POST — Échange le jeton de rafraîchissement contre une nouvelle paire (rotation). 401 si invalide.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(request.RefreshToken, ct));

    /// <summary>POST — Déconnexion : révoque le jeton de rafraîchissement (anonyme : le jeton d'accès peut avoir expiré).</summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

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

    /// <summary>
    /// GET — Installation vierge ? (aucun compte utilisateur). Anonyme : c'est cet appel qui decide
    /// si le client affiche l'ecran de creation du compte administrateur initial.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("is-first-run")]
    public async Task<ActionResult<bool>> IsFirstRun(CancellationToken ct)
        => Ok(await _auth.IsFirstRunAsync(ct));

    /// <summary>
    /// POST — Cree le premier compte administrateur. Anonyme, mais refuse dès qu'un compte existe.
    /// Envoie les identifiants par e-mail si la configuration SMTP est renseignee.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("create-admin")]
    [ProducesResponseType(typeof(AdminSetupResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminSetupResultDto>> CreateAdmin([FromBody] AdminSetupDto dto, CancellationToken ct)
        => Ok(await _auth.CreateAdminAsync(dto, ct));

    /// <summary>
    /// GET — Politique de mot de passe (anonyme) : règles et libellés utilisés par l'écran de
    /// démarrage et l'écran Utilisateurs pour valider avant l'enregistrement.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("password-policy")]
    [ProducesResponseType(typeof(PasswordPolicyDto), StatusCodes.Status200OK)]
    public ActionResult<PasswordPolicyDto> GetPasswordPolicy() => Ok(PasswordPolicy.Describe());
}

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
        // Confirmation requise : RoleChangeConfirmationRequiredException → 409 avec l'aperçu en corps (middleware global).
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

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _auth.GetRolesPagedAsync(page, pageSize, search, ct));

    [RequireAdmin]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RoleDto dto, CancellationToken ct)
        => Ok(await _auth.SaveRoleAsync(dto with { Id = 0 }, ct));

    [RequireAdmin]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] RoleDto dto, CancellationToken ct)
        => Ok(await _auth.SaveRoleAsync(dto with { Id = id }, ct));

    [RequireAdmin]
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

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[Route("api/privileges/audit")]
[Tags("Privileges")]
public class AuditDroitController : ControllerBase
{
    private readonly AuthService _auth;
    public AuditDroitController(AuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? typeCible,
        [FromQuery] decimal? idCible,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _auth.GetAuditDroitsAsync(typeCible, idCible, from, to, ct));
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

    [RequireAdmin]
    [HttpPut("{userId}")]
    public async Task<IActionResult> Replace(decimal userId, [FromBody] List<int> sageDbIds, CancellationToken ct)
    {
        await _auth.SetDbAccessAsync(userId, sageDbIds, ct);
        return NoContent();
    }
}
