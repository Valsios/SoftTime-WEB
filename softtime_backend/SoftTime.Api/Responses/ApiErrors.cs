using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SoftTime.Api.Responses;

/// <summary>Réponses d'échec au format <see cref="ApiResponse{TData}"/>, partagées par les filtres et le middleware.</summary>
public static class ApiErrors
{
    public const string InvalidRequestMessage = "Requête invalide.";

    /// <summary>Échec simple, éventuellement rattaché à un champ du formulaire.</summary>
    public static ApiResponse<object?> Fail(string message, string? field = null)
        => ApiResponse<object?>.FailResult(message) with { Field = field };

    /// <summary>Résultat d'action d'échec avec le code HTTP donné.</summary>
    public static ObjectResult Result(int statusCode, string message, string? field = null)
        => new(Fail(message, field)) { StatusCode = statusCode };

    /// <summary>400 de validation MVC (liaison de modèle) : erreurs détaillées par champ.</summary>
    public static BadRequestObjectResult FromModelState(ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
        return new BadRequestObjectResult(ApiResponse<object?>.FailResult(InvalidRequestMessage) with { Errors = errors });
    }
}
