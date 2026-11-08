using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Api.Responses;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Moteur prédictif : état, prévisions d'heures supplémentaires, déclenchement
/// des traitements. La lecture est ouverte à tout utilisateur authentifié disposant de
/// <c>REPORTS:PREDICTIONS</c> ; les traitements sont réservés au droit Paramètres (ou admin).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/predictions")]
[Tags("Prévisions")]
public class PredictionsController : ApiControllerBase
{
    private readonly IMlForecastQueryService _predictions;
    private readonly MlJobCoordinator _jobs;
    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;
    private readonly IEffectiveRightsService _rights;

    public PredictionsController(
        IMlForecastQueryService predictions,
        MlJobCoordinator jobs,
        ICurrentUser user,
        ICompanyContext company,
        IEffectiveRightsService rights)
    {
        _predictions = predictions;
        _jobs = jobs;
        _user = user;
        _company = company;
        _rights = rights;
    }

    [HttpGet("status")]
    public async Task<IActionResult> StatusAsync(CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(await _predictions.GetStatusAsync(ct));
    }

    [HttpGet("job")]
    public async Task<IActionResult> JobAsync(CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(_jobs.State);
    }

    [HttpGet("overtime")]
    public async Task<IActionResult> OvertimeAsync([FromQuery] DateTime? week, CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(await _predictions.GetHsForecastsAsync((week ?? DateTime.Today).Date, ct));
    }

    /// <summary>Totaux HS prévus par semaine sur l'horizon.</summary>
    [HttpGet("overtime/horizon")]
    public async Task<IActionResult> OvertimeHorizonAsync(CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(await _predictions.GetHsHorizonAsync(ct));
    }

    /// <summary>Prévu / réel des dernières semaines terminées.</summary>
    [HttpGet("overtime/accuracy")]
    public async Task<IActionResult> OvertimeAccuracyAsync([FromQuery] int? weeks, CancellationToken ct)
    {
        await EnsureCanReadAsync(ct);
        return Ok(await _predictions.GetHsAccuracyAsync(weeks ?? 8, ct));
    }

    /// <summary>Rafraîchit la fenêtre glissante (12 semaines par défaut) et régénère les prévisions.</summary>
    [RequireAdmin]
    [HttpPost("refresh")]
    public IActionResult Refresh() => Enqueue(new MlJobRequest(_company.SageDatabase, FullRefresh: false, Train: false),
        "Rafraîchissement des données et des prévisions programmé.");

    /// <summary>Reconstruit tout l'historique (premier déploiement), réentraîne et régénère.</summary>
    [RequireAdmin]
    [HttpPost("backfill")]
    public IActionResult Backfill() => Enqueue(new MlJobRequest(_company.SageDatabase, FullRefresh: true, Train: true),
        "Reconstruction complète de la base analytique programmée.");

    /// <summary>Réentraîne les modèles actifs.</summary>
    [RequireAdmin]
    [HttpPost("train")]
    public IActionResult Train() => Enqueue(new MlJobRequest(_company.SageDatabase, FullRefresh: false, Train: true),
        "Réentraînement programmé.");

    /// <summary>Lecture des prévisions : administrateur, ou porte 6 ET permission REPORTS:PREDICTIONS effective.</summary>
    private Task EnsureCanReadAsync(CancellationToken ct)
        => FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, "REPORTS:PREDICTIONS", ct);

    private IActionResult Enqueue(MlJobRequest job, string message)
    {
        AdminAccess.Require(_user);

        return _jobs.TryEnqueue(job)
            ? Accepted(new { message })
            : ApiErrors.Result(StatusCodes.Status409Conflict, "Un traitement ML est déjà en cours.");
    }
}
