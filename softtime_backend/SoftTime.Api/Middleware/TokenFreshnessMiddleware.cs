using Microsoft.AspNetCore.Authorization;

using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Security;

namespace SoftTime.Api.Middleware;

/// <summary>
/// Refuse (401) un jeton d'accès dont l'empreinte des droits n'est plus celle du compte : droits, rôle ou accès aux
/// bases modifiés depuis son émission, ou jeton émis avant l'empreinte. Le client renouvelle alors sa session et
/// rejoue l'appel avec un jeton à jour : un retrait de droit s'applique dès la requête suivante.
/// Ne concerne que les routes protégées ([Authorize] sans [AllowAnonymous]). Échec fermé (403) si les droits sont illisibles.
/// </summary>
public sealed class TokenFreshnessMiddleware
{
    public const string StaleTokenMessage = "Vos droits ont changé : la session doit être renouvelée.";

    private readonly RequestDelegate _next;

    public TokenFreshnessMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IEffectiveRightsService effectiveRights)
    {
        if (IsProtected(context) && currentUser.UserId is decimal userId)
        {
            var rights = await LoadRightsOrDenyAsync(effectiveRights, userId, context.RequestAborted);
            if (!RightsStamp.IsCurrent(PermissionClaims.ReadStamp(context.User), rights))
                throw new UnauthorizedException(StaleTokenMessage);
        }
        await _next(context);
    }

    private static bool IsProtected(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return false;
        var metadata = context.GetEndpoint()?.Metadata;
        return metadata?.GetMetadata<IAuthorizeData>() is not null
               && metadata.GetMetadata<IAllowAnonymous>() is null;
    }

    private static async Task<EffectiveRights> LoadRightsOrDenyAsync(
        IEffectiveRightsService effectiveRights, decimal userId, CancellationToken ct)
    {
        try
        {
            return await effectiveRights.GetAsync(userId, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            effectiveRights.LogReadFailure(userId, ex);
            throw new ForbiddenException(ReadPermissions.Denied);
        }
    }
}
