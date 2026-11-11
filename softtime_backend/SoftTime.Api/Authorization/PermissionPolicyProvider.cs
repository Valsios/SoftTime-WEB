using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Fabrique à la demande la règle d'autorisation d'un <see cref="HasPermissionAttribute"/> à partir de son nom
/// (« Permission:CODE|CODE ») : aucune liste de règles à tenir à jour au démarrage.
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return base.GetPolicyAsync(policyName);

        var permissions = policyName[HasPermissionAttribute.PolicyPrefix.Length..]
            .Split(HasPermissionAttribute.Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
