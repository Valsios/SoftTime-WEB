using Microsoft.AspNetCore.Mvc;

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
