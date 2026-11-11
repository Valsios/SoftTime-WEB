using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.Permissions;

namespace SoftTime.Tests.Http;

/// <summary>
/// Les jetons de test sont fabriqués à la main, sans passer par la connexion : ce filtre leur ajoute, comme le ferait
/// une connexion à l'instant, les permissions et l'empreinte des droits du compte. Un jeton invalide (signature,
/// expiration) ou qui porte déjà une empreinte est transmis tel quel : les tests de jeton périmé gardent la main.
/// </summary>
internal sealed class FreshTokenStartupFilter : IStartupFilter
{
    private const string Scheme = "Bearer ";

    private static readonly HashSet<string> s_technicalClaims = new(StringComparer.Ordinal) { "exp", "nbf", "iat", "iss", "aud" };

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, following) =>
        {
            await RestampAsync(context);
            await following(context);
        });
        next(app);
    };

    private static async Task RestampAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Scheme, StringComparison.Ordinal) || !ApiFactory.IsValidToken(header[Scheme.Length..]))
            return;
        var token = new JwtSecurityTokenHandler().ReadJwtToken(header[Scheme.Length..]);
        if (token.Claims.Any(c => c.Type == PermissionClaims.RightsStamp)
            || !decimal.TryParse(token.Claims.FirstOrDefault(c => c.Type == "uid")?.Value, out var userId))
            return;
        try
        {
            var rights = await context.RequestServices.GetRequiredService<IEffectiveRightsService>().GetAsync(userId);
            var claims = token.Claims.Where(c => !s_technicalClaims.Contains(c.Type))
                .Append(new Claim(PermissionClaims.Permissions, PermissionClaims.Join(rights.TokenPermissions)))
                .Append(new Claim(PermissionClaims.RightsStamp, rights.Stamp));
            context.Request.Headers.Authorization = Scheme + ApiFactory.Mint(claims, token.ValidTo);
        }
        catch (Exception)
        {
            // Droits illisibles (magasin en panne simulée) : le jeton part tel quel, le serveur doit refuser.
        }
    }
}
