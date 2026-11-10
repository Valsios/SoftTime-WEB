using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Api.Authorization;

public class RequireDroitFilter : IAuthorizationFilter
{
    private readonly int _droit;
    private readonly ICurrentUser _user;
    public RequireDroitFilter(int droit, ICurrentUser user)
    {
        _droit = droit;
        _user = user;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return;
        if (!_user.HasRight(_droit))
            context.Result = new ForbidResult();
    }
}
