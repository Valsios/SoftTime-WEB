using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Comme <see cref="RequireDroitAttribute"/> (même refus : 403) mais satisfait par UNE des portes données.
/// L'administrateur les a toutes (<c>ICurrentUser.HasRight</c>). Un compte sans aucune de ces portes est refusé.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAnyDroitAttribute : TypeFilterAttribute
{
    public RequireAnyDroitAttribute(params int[] droits) : base(typeof(RequireAnyDroitFilter))
    {
        Arguments = new object[] { droits };
    }
}

public class RequireAnyDroitFilter : IAuthorizationFilter
{
    private readonly int[] _droits;
    private readonly Application.Abstractions.ICurrentUser _user;

    public RequireAnyDroitFilter(int[] droits, Application.Abstractions.ICurrentUser user)
    {
        _droits = droits;
        _user = user;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return;
        if (!_droits.Any(_user.HasRight))
            context.Result = new ForbidResult();
    }
}
