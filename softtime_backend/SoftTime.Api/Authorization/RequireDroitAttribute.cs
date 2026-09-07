using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SoftTime.Domain;

namespace SoftTime.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireDroitAttribute : TypeFilterAttribute
{
    public RequireDroitAttribute(int droit) : base(typeof(RequireDroitFilter))
    {
        Arguments = new object[] { droit };
    }
}

public class RequireDroitFilter : IAuthorizationFilter
{
    private readonly int _droit;
    private readonly Application.Abstractions.ICurrentUser _user;
    public RequireDroitFilter(int droit, Application.Abstractions.ICurrentUser user)
    {
        _droit = droit;
        _user = user;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return;
        if (!_user.HasRight(_droit))
            context.Result = new ForbidResult();
    }
}

public static class Droit
{
    public const int Databases = Rights.Databases;
    public const int Parameters = Rights.Parameters;
    public const int Processing = Rights.Processing;
    public const int Other = Rights.Other;
    public const int Traceability = Rights.Traceability;
    public const int Reports = Rights.Reports;
}
