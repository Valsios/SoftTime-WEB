using Microsoft.AspNetCore.Authorization;

using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Accorde l'accès à l'administrateur, ou au compte dont le jeton porte une des permissions demandées. Le jeton est
/// fiable ici : <c>TokenFreshnessMiddleware</c> a déjà refusé tout jeton dont les droits ne sont plus à jour.
/// Une exigence sans permission n'est jamais satisfaite (échec fermé).
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUser _currentUser;

    public PermissionAuthorizationHandler(ICurrentUser currentUser) => _currentUser = currentUser;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        if (AdminAccess.IsAdmin(_currentUser))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var granted = PermissionClaims.ReadPermissions(context.User);
        if (requirement.AnyOf.Any(granted.Contains))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
