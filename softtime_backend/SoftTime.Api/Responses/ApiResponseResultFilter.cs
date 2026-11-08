using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SoftTime.Api.Responses;

/// <summary>
/// Enveloppe toute réponse de succès portant des données dans <see cref="ApiResponse{TData}"/> (§7.3). Les réponses
/// sans corps (204, 200 vide), les fichiers et les réponses déjà enveloppées sont laissées telles quelles.
/// </summary>
public sealed class ApiResponseResultFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult result
            && (result.StatusCode ?? StatusCodes.Status200OK) is >= 200 and < 300
            && !IsEnveloped(result.Value))
        {
            var envelope = ApiResponse<object?>.SuccessResult(result.Value);
            context.Result = new ObjectResult(envelope) { StatusCode = result.StatusCode };
        }
        return next();
    }

    private static bool IsEnveloped(object? value)
        => value is not null && value.GetType().IsGenericType
           && value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>);
}
