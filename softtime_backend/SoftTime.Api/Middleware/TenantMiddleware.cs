using System.Security.Claims;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICompanyContext company, ICurrentUser user, TenantConnectionService tenant)
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
        if (!skip && context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(sage))
        {
            try
            {
                await tenant.EnsureAuthorizedAsync(context.RequestAborted);
            }
            catch (UnauthorizedAccessException)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Base SAGE non autorisée." });
                return;
            }
        }
        await _next(context);
    }
}

public class ExceptionHandlingMiddleware
{
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
        catch (UnauthorizedAccessException ex)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Erreur interne." });
        }
    }
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
