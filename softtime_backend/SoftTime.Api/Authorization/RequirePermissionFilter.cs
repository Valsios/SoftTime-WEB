using Microsoft.AspNetCore.Mvc.Filters;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Security;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Refuse (403, message de <see cref="FinePermissionAccess.ActionDenied"/>) un compte authentifié dont les droits
/// effectifs n'ont pas la porte et la permission fine demandées. Les comptes non authentifiés sont laissés à
/// [Authorize] (401).
/// </summary>
public class RequirePermissionFilter : IAsyncAuthorizationFilter
{
    private readonly FinePermission _required;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;

    public RequirePermissionFilter(int droit, string permission, ICurrentUser user, IEffectiveRightsService rights)
    {
        _required = new FinePermission(droit, permission);
        _user = user;
        _rights = rights;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;
        return FinePermissionAccess.EnsureAsync(
            _user, _rights, _required, FinePermissionAccess.ActionDenied, context.HttpContext.RequestAborted);
    }
}
