using System.Security.Claims;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Application.Services;

namespace SoftTime.Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICompanyContext company, ICurrentUser user, ITenantConnectionService tenant)
    {
        var sage = context.Request.Headers["X-Sage-Database"].FirstOrDefault();
        var pte = context.Request.Headers["X-Pointeuse-Database"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sage))
            company.Set(sage, pte);

        var path = context.Request.Path.Value ?? string.Empty;
        var skip = path.Contains("/api/auth", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/users", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/roles", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/privileges", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/db-access", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/sage-databases", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/pointeuse-databases", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/meta", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/swagger", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/health", StringComparison.OrdinalIgnoreCase);
        // Base refusée : ForbiddenException, traduite en 403 par ExceptionHandlingMiddleware.
        if (!skip && context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(sage))
            await tenant.EnsureAuthorizedAsync(context.RequestAborted);
        await _next(context);
    }
}

/// <summary>
/// Middleware global des erreurs (convention §6.1.1) : chaque exception métier (<see cref="DomainException"/>
/// et ses sous-types) devient un code HTTP avec son message ; toute autre exception est un bug, journalisée
/// avec sa pile et renvoyée en 500 sans aucun détail technique.
/// </summary>
public class ExceptionHandlingMiddleware
{
    public const string InternalErrorMessage = "Erreur interne.";

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
            // Client parti : rien à répondre ni à signaler.
        }
        catch (DomainException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodeOf(ex);
            await context.Response.WriteAsJsonAsync(BodyOf(ex));
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            _logger.LogError(ex, "Erreur non gérée sur {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = InternalErrorMessage });
        }
    }

    public static int StatusCodeOf(DomainException exception) => exception switch
    {
        NotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedException => StatusCodes.Status401Unauthorized,
        ForbiddenException => StatusCodes.Status403Forbidden,
        ConflictException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    private static object BodyOf(DomainException exception) => exception switch
    {
        FieldValidationException field => new { error = field.Message, field = field.Field },
        ConflictException { Details: { } details } => details,
        _ => new { error = exception.Message }
    };
}

public class DroitRequirementAttribute : Attribute
{
    public int Droit { get; }
    public DroitRequirementAttribute(int droit) => Droit = droit;
}

public class DroitFilter : IEndpointFilter
{
    private readonly int _droit;
    public DroitFilter(int droit) => _droit = droit;
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!user.HasRight(_droit))
            return Results.Forbid();
        return await next(context);
    }
}
