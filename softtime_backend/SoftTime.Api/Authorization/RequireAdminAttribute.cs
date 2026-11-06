using Microsoft.AspNetCore.Mvc;

namespace SoftTime.Api.Authorization;

/// <summary>Réserve une action aux administrateurs (rôle Administrateur) : 403 avec message sinon.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute : TypeFilterAttribute
{
    public RequireAdminAttribute() : base(typeof(RequireAdminFilter)) { }
}
