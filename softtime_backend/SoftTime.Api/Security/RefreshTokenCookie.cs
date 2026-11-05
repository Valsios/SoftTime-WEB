using SoftTime.Application.DTOs;
using SoftTime.Application.Security;

namespace SoftTime.Api.Security;

/// <summary>
/// Le jeton de rafraîchissement voyage dans un cookie <c>HttpOnly</c> : le JavaScript de la page ne peut pas le lire,
/// donc une faille XSS ne permet pas de le voler pour ouvrir une session de 7 jours ailleurs. <c>SameSite=Strict</c> :
/// un autre site ne peut pas déclencher de rafraîchissement ni de déconnexion avec ce cookie. <c>Path=/</c> : l'API peut
/// être servie derrière un préfixe (proxy de développement, réécriture IIS en production) que le serveur ne connaît pas.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "st_refresh";

    /// <summary>Pose le cookie et retire le jeton du corps de la réponse (il n'est jamais exposé au JavaScript).</summary>
    public static LoginResponse Issue(HttpContext context, LoginResponse response, AccountSecurityOptions options)
    {
        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            context.Response.Cookies.Append(Name, response.RefreshToken, Options(context, DateTimeOffset.UtcNow.AddDays(options.RefreshTokenDays)));
        }
        return response with { RefreshToken = null };
    }

    /// <summary>Jeton présenté : le cookie, sinon le corps (clients hors navigateur, anciens clients).</summary>
    public static string? Read(HttpContext context, string? fromBody)
        => context.Request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : fromBody;

    public static void Delete(HttpContext context)
        => context.Response.Cookies.Delete(Name, Options(context, null));

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Hors HTTPS (développement en http://localhost), un cookie Secure ne serait jamais renvoyé par le navigateur.
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true
    };
}
