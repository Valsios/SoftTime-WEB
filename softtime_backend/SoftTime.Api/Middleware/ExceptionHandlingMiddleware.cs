using SoftTime.Application.Services;

namespace SoftTime.Api.Middleware;

/// <summary>
/// Traduit les exceptions typées du domaine en codes HTTP (§6.1.1). Seul le message d'une exception du domaine,
/// écrit pour l'utilisateur, est renvoyé au client ; toute autre exception (EF Core, SqlClient, .NET) est journalisée
/// et masquée derrière un 500 générique, car son message peut révéler le schéma, un chemin ou un compte.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const string InternalErrorMessage = "Erreur interne.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client parti en cours de requête : rien à répondre ni à journaliser comme une erreur.
        }
        catch (DomainException ex)
        {
            await WriteDomainErrorAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur non gérée sur {Method} {Path}.", context.Request.Method, context.Request.Path.Value);
            if (context.Response.HasStarted)
                throw;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = InternalErrorMessage });
        }
    }

    private static Task WriteDomainErrorAsync(HttpContext context, DomainException exception)
    {
        context.Response.StatusCode = StatusCodeOf(exception);
        return exception switch
        {
            // Même corps que l'aperçu : le client demande confirmation puis renvoie confirmClearAdjustments = true.
            RoleChangeConfirmationRequiredException roleChange => context.Response.WriteAsJsonAsync(roleChange.Preview),
            FieldValidationException field => context.Response.WriteAsJsonAsync(new { error = field.Message, field = field.Field }),
            _ => context.Response.WriteAsJsonAsync(new { error = exception.Message })
        };
    }

    private static int StatusCodeOf(DomainException exception) => exception switch
    {
        NotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedException => StatusCodes.Status401Unauthorized,
        ForbiddenException => StatusCodes.Status403Forbidden,
        ConflictException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}
