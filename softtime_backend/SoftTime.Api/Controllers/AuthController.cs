using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using SoftTime.Api.Security;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;

namespace SoftTime.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _auth;
    private readonly ICurrentUser _user;
    private readonly AccountSecurityOptions _security;
    public AuthController(IAuthenticationService auth, ICurrentUser user, IOptions<AccountSecurityOptions> security)
    {
        _auth = auth;
        _user = user;
        _security = security.Value;
    }

    /// <summary>
    /// POST — Authentification (LOGIN/PASSWORD T_RESPONSABLE). Retourne le jeton d'accès (courte durée) ; le jeton
    /// de rafraîchissement est posé dans un cookie HttpOnly (voir RefreshTokenCookie), jamais dans le corps.
    /// Compte verrouillé après plusieurs échecs (403), requêtes limitées par adresse IP (429).
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(RefreshTokenCookie.Issue(HttpContext, await _auth.LoginAsync(request, ct), _security));

    /// <summary>
    /// POST — Échange le jeton de rafraîchissement (cookie, sinon corps) contre une nouvelle paire (rotation). 401 si invalide.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request, CancellationToken ct)
    {
        // Un refus (401) n'efface pas le cookie : quand deux onglets rafraîchissent en même temps, le refus du second
        // effacerait le nouveau cookie que le premier vient de recevoir (le navigateur partage un seul cookie).
        var response = await _auth.RefreshAsync(RefreshTokenCookie.Read(HttpContext, request?.RefreshToken), ct);
        return Ok(RefreshTokenCookie.Issue(HttpContext, response, _security));
    }

    /// <summary>POST — Déconnexion : révoque le jeton de rafraîchissement (anonyme : le jeton d'accès peut avoir expiré).</summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit.Policy)]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request, CancellationToken ct)
    {
        await _auth.LogoutAsync(RefreshTokenCookie.Read(HttpContext, request?.RefreshToken), ct);
        RefreshTokenCookie.Delete(HttpContext);
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
