using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Middleware;

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
