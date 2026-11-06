namespace SoftTime.Api.Middleware;

/// <summary>
/// En-têtes de sécurité HTTP sur chaque réponse de l'API. L'API ne sert que du JSON et des fichiers :
/// la CSP interdit tout chargement de ressource et tout encadrement. Swagger (développement seulement)
/// a besoin de scripts et de styles : la CSP stricte ne lui est pas appliquée.
/// </summary>
public class SecurityHeadersMiddleware
{
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!context.Request.Path.StartsWithSegments("/swagger"))
            headers["Content-Security-Policy"] = ApiContentSecurityPolicy;
        return _next(context);
    }
}
