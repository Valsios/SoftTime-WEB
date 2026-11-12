using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Api.Middleware;

/// <summary>
/// Lot 5 : pour un compte authentifié, lit ses droits effectifs (rôle actuel et portes) et les pose sur la requête.
/// <see cref="CurrentUser"/> les utilise à la place du jeton : un droit retiré ou un administrateur rétrogradé est
/// refusé dès l'invalidation du cache, sans attendre l'expiration du jeton. Échec fermé si la lecture échoue.
/// </summary>
public sealed class ServerRightsMiddleware
{
    private readonly RequestDelegate _next;

    public ServerRightsMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentUser user, IEffectiveRightsService rights)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            context.Items[ServerRights.ItemKey] = await LoadAsync(user, rights, context.RequestAborted);
        await _next(context);
    }

    private static async Task<ServerRights> LoadAsync(ICurrentUser user, IEffectiveRightsService rights, CancellationToken ct)
    {
        if (user.UserId is not decimal uid)
            return ServerRights.None;
        try
        {
            var effective = await rights.GetAsync(uid, ct: ct);
            return new ServerRights(effective.RoleId, effective.Gates.ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rights.LogReadFailure(uid, ex);
            return ServerRights.None;
        }
    }
}
