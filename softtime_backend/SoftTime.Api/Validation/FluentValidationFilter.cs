using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

using FluentValidation;

using SoftTime.Api.Responses;

namespace SoftTime.Api.Validation;

/// <summary>
/// Valide chaque argument d'action pour lequel un <see cref="IValidator{T}"/> est enregistré (validateurs de
/// SoftTime.Application). En cas d'échec : 400 <c>ApiResponse</c> avec <c>message</c> et <c>field</c>, la même forme que FieldValidationException,
/// que l'interface affiche sous le champ concerné. L'action n'est pas exécutée.
/// </summary>
public sealed class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
                continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
                continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid)
                continue;

            var failure = result.Errors[0];
            context.Result = new BadRequestObjectResult(ApiErrors.Fail(failure.ErrorMessage, CamelCase(failure.PropertyName)));
            return;
        }
        await next();
    }

    private static string CamelCase(string propertyName)
        => string.IsNullOrEmpty(propertyName) ? propertyName : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
