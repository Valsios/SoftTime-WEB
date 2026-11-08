using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Responses;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Api.Authorization;

public class RequireAdminFilter : IAuthorizationFilter
{
    private readonly ICurrentUser _user;
    public RequireAdminFilter(ICurrentUser user) => _user = user;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return;
        if (!AdminAccess.IsAdmin(_user))
            context.Result = new ObjectResult(ApiErrors.Fail(AdminAccess.ReservedMessage))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
    }
}
