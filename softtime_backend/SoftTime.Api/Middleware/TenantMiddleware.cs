using SoftTime.Application.Abstractions.RequestContext;

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
        var skip = path.Contains("/api/v1/auth", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/users", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/roles", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/privileges", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/db-access", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/sage-databases", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/pointeuse-databases", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/api/v1/meta", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/swagger", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/health", StringComparison.OrdinalIgnoreCase);
        if (!skip && context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(sage))
        {
            await tenant.EnsureAuthorizedAsync(context.RequestAborted);
        }
        await _next(context);
    }
}
