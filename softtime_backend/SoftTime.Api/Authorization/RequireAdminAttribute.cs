using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Api.Authorization;

/// <summary>Réserve une action aux administrateurs (rôle Administrateur) : 403 avec message sinon.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute : TypeFilterAttribute
{
    public RequireAdminAttribute() : base(typeof(RequireAdminFilter)) { }
}

public class RequireAdminFilter : IAuthorizationFilter
{
    private readonly ICurrentUser _user;
    public RequireAdminFilter(ICurrentUser user) => _user = user;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return;
        if (!AdminAccess.IsAdmin(_user))
            context.Result = new ObjectResult(new { error = AdminAccess.ReservedMessage })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
    }
}
