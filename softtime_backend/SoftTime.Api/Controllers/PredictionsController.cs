using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Moteur prédictif : état, prévisions d'absence et d'heures supplémentaires, déclenchement
/// des traitements. La lecture est ouverte à tout utilisateur authentifié disposant de
/// <c>REPORTS:PREDICTIONS</c> ; les traitements sont réservés au droit Paramètres (ou admin).
/// </summary>
[ApiController]
[Authorize]
[Route("api/predictions")]
[Tags("Prévisions")]
public class PredictionsController : ControllerBase
{
    private readonly MlPredictionService _predictions;
    private readonly MlJobCoordinator _jobs;
    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;

    public PredictionsController(
        MlPredictionService predictions,
        MlJobCoordinator jobs,
        ICurrentUser user,
        ICompanyContext company)
    {
        _predictions = predictions;
        _jobs = jobs;
        _user = user;
        _company = company;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
        => CanRead() ? Ok(await _predictions.GetStatusAsync(ct)) : Forbid();

    [HttpGet("job")]
    public IActionResult Job() => CanRead() ? Ok(_jobs.State) : Forbid();

    [HttpGet("absence")]
    public async Task<IActionResult> Absence(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? departement,
        [FromQuery] int? categorie,
        CancellationToken ct)
    {
        if (!CanRead())
            return Forbid();

        var debut = (from ?? DateTime.Today).Date;
        var fin = (to ?? debut.AddDays(14)).Date;
        if (fin < debut)
            fin = debut;
        return Ok(await _predictions.GetAbsenceForecastsAsync(debut, fin, departement, categorie, ct));
    }

    [HttpGet("overtime")]
    public async Task<IActionResult> Overtime([FromQuery] DateTime? week, CancellationToken ct)
        => CanRead()
            ? Ok(await _predictions.GetHsForecastsAsync((week ?? DateTime.Today).Date, ct))
            : Forbid();

    /// <summary>Rafraîchit la fenêtre glissante (12 semaines par défaut) et régénère les prévisions.</summary>
    [HttpPost("refresh")]
    public IActionResult Refresh() => Enqueue(new MlJobRequest(_company.SageDatabase, FullRefresh: false, Train: false),
        "Rafraîchissement des données et des prévisions programmé.");

    /// <summary>Reconstruit tout l'historique (premier déploiement), réentraîne et régénère.</summary>
    [HttpPost("backfill")]
    public IActionResult Backfill() => Enqueue(new MlJobRequest(_company.SageDatabase, FullRefresh: true, Train: true),
        "Reconstruction complète de la base analytique programmée.");

    /// <summary>Réentraîne les modèles actifs.</summary>
    [HttpPost("train")]
    public IActionResult Train() => Enqueue(new MlJobRequest(_company.SageDatabase, FullRefresh: false, Train: true),
        "Réentraînement programmé.");

    private bool CanRead() => _user.HasRight(Rights.Reports);

    private IActionResult Enqueue(MlJobRequest job, string message)
    {
        if (!_user.HasRight(Rights.Parameters))
            return Forbid();

        return _jobs.TryEnqueue(job)
            ? Accepted(new { message })
            : Conflict(new { message = "Un traitement ML est déjà en cours." });
    }
}
