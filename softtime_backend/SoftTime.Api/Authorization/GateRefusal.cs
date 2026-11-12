using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Responses;

namespace SoftTime.Api.Authorization;

/// <summary>Refus d'un compte authentifié qui n'a pas la porte demandée : 403 avec un message, jamais un corps vide.</summary>
public static class GateRefusal
{
    public const string Message = "Vous n'avez pas accès à cette section.";

    public static IActionResult Result() => new ObjectResult(ApiErrors.Fail(Message))
    {
        StatusCode = StatusCodes.Status403Forbidden
    };
}
