using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Application.Abstractions.Landing;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Security;
using SoftTime.Domain;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Prévisions d'heures supplémentaires : atterrissage de la semaine, calculé à la demande à partir
/// des pointages traités et du planning. Lecture seule, ouverte à tout utilisateur authentifié
/// disposant de <c>predictions.read</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/predictions")]
[Tags("Prévisions")]
public class PredictionsController : ApiControllerBase
{
    private readonly IWeekLandingService _landing;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;

    public PredictionsController(IWeekLandingService landing, ICurrentUser user, IEffectiveRightsService rights)
    {
        _landing = landing;
        _user = user;
        _rights = rights;
    }

    /// <summary>Atterrissage par salarié de la semaine demandée (semaine en cours par défaut).</summary>
    [HttpGet("landing")]
    public async Task<IActionResult> LandingAsync([FromQuery] DateTime? week, CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(await _landing.GetAsync((week ?? DateTime.Today).Date, ct));
    }

    /// <summary>Totaux d'atterrissage de la semaine en cours et des semaines suivantes.</summary>
    [HttpGet("landing/horizon")]
    public async Task<IActionResult> LandingHorizonAsync(CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(await _landing.GetHorizonAsync(ct));
    }

    /// <summary>Lecture des prévisions : administrateur, ou porte 6 ET permission predictions.read effective.</summary>
    private Task EnsureCanReadAsync(CancellationToken ct)
        => FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, "predictions.read", ct);
}
