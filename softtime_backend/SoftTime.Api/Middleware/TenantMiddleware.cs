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
            await tenant.EnsureAuthorizedAsync(context.RequestAborted);
        }
        await _next(context);
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
