using System.Security.Claims;

namespace SoftTime.Api.Middleware;

/// <summary>
/// Journalise chaque accès refusé (403) pour repérer les tentatives : compte, méthode et MODÈLE de route
/// (« api/v1/users/{id} »), jamais le chemin réel, la requête ni le corps (aucune donnée de salarié ni secret).
/// Placé en tête du pipeline : il voit aussi les 403 produits par le gestionnaire d'exceptions.
/// </summary>
public sealed class ForbiddenAccessLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ForbiddenAccessLoggingMiddleware> _logger;

    public ForbiddenAccessLoggingMiddleware(RequestDelegate next, ILogger<ForbiddenAccessLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);
        if (context.Response.StatusCode != StatusCodes.Status403Forbidden)
            return;
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(route inconnue)";
        var account = context.User.FindFirstValue("uid") ?? "(anonyme)";
        _logger.LogWarning("Accès refusé (403) : compte {Account}, {Method} {Route}.", account, context.Request.Method, route);
    }
}
