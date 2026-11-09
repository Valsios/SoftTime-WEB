using Microsoft.AspNetCore.Mvc;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Exige une permission fine côté serveur (en plus de la porte de la classe), par exemple
/// <c>[RequirePermission(Droit.Processing, "PROC:SHIFTS:DELETE")]</c>. L'administrateur passe toujours.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(int droit, string permission) : base(typeof(RequirePermissionFilter))
    {
        Arguments = new object[] { droit, permission };
    }
}
