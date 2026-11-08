using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Responses;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Base des controllers de l'API : réponses JSON et codes documentés dans Swagger.
/// Toute réponse JSON, succès comme échec, suit l'enveloppe <see cref="ApiResponse{TData}"/>
/// (les succès sont enveloppés par <see cref="ApiResponseResultFilter"/>).
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
}
