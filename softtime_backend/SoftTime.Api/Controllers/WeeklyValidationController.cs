using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/weekly-validation")]
[Tags("WeeklyValidation")]
public class WeeklyValidationController : ControllerBase
{
    private readonly IWeeklyValidationService _svc;
    public WeeklyValidationController(IWeeklyValidationService svc) => _svc = svc;

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.PreviewWeekAsync(request, ct));

    /// <summary>
    /// Aperçu paginé (lecture seule) : même calcul que <c>preview</c>, sans persistance des lignes
    /// non validées, afin de ne pas écrire à chaque changement de page.
    /// </summary>
    [HttpPost("preview/paged")]
    public async Task<IActionResult> PreviewPaged(
        [FromBody] PeriodRequest request,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => Ok(await _svc.PreviewWeekPagedAsync(request, page, pageSize, ct));

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] WeeklyValidationRequest request, CancellationToken ct)
    {
        await _svc.ValidateWeekAsync(request, true, ct);
        return NoContent();
    }

    [HttpPost("unvalidate")]
    public async Task<IActionResult> Unvalidate([FromBody] WeeklyValidationRequest request, CancellationToken ct)
    {
        await _svc.ValidateWeekAsync(request, false, ct);
        return NoContent();
    }

    /// <summary>Purge la validation de la semaine (supprime T_HS_VALIDER et déverrouille les heures).</summary>
    [HttpDelete]
    public async Task<IActionResult> Purge([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.PurgeValidationAsync(filter, ct));
}
