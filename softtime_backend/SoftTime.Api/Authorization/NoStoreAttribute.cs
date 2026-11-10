using Microsoft.AspNetCore.Mvc.Filters;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Interdit toute conservation de la réponse (navigateur partagé, mandataire) : Cache-Control: no-store et Pragma:
/// no-cache. Les en-têtes sont posés au démarrage de la réponse, donc aussi sur les refus (403), les erreurs de saisie
/// (400) et le 404 de la fonction désactivée, y compris quand un intergiciel d erreurs reconstruit la réponse. Réservé
/// aux routes personnelles.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoStoreAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    /// <summary>Premier filtre de la chaîne : posé avant tout refus d autorisation.</summary>
    public int Order => int.MinValue;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        context.HttpContext.Response.OnStarting(static etat =>
        {
            var entetes = ((HttpResponse)etat).Headers;
            entetes.CacheControl = "no-store";
            entetes.Pragma = "no-cache";
            return Task.CompletedTask;
        }, context.HttpContext.Response);
    }
}
