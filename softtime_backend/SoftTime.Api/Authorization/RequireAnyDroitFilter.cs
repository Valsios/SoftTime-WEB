using Microsoft.AspNetCore.Mvc.Filters;

using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Api.Authorization;

/// <summary>Refuse (403) un compte authentifié qui n'a aucune des portes demandées.</summary>
public class RequireAnyDroitFilter : IAuthorizationFilter
{
    private readonly int[] _droits;
    private readonly ICurrentUser _user;

    public RequireAnyDroitFilter(int[] droits, ICurrentUser user)
    {
        _droits = droits;
        _user = user;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return;
        if (!_droits.Any(_user.HasRight))
            context.Result = GateRefusal.Result();
    }
}
