using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Moteur prédictif : état, prévisions d'absence et d'heures supplémentaires, déclenchement
/// des traitements. La lecture exige <c>REPORTS:PREDICTIONS</c> (ou l'administrateur) ; les traitements
/// sont réservés à l'administrateur (409 si un traitement est déjà en cours).
/// </summary>
[ApiController]
[Authorize]
[Route("api/predictions")]
[Tags("Prévisions")]
public class PredictionsController : ControllerBase
{
    private readonly IPredictionsService _predictions;

    public PredictionsController(IPredictionsService predictions) => _predictions = predictions;

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _predictions.GetStatusAsync(ct));

    [HttpGet("job")]
    public async Task<IActionResult> Job(CancellationToken ct) => Ok(await _predictions.GetJobAsync(ct));

    [HttpGet("absence")]
    public async Task<IActionResult> Absence(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? departement,
        [FromQuery] int? categorie,
        CancellationToken ct)
        => Ok(await _predictions.GetAbsenceAsync(from, to, departement, categorie, ct));

    [HttpGet("overtime")]
    public async Task<IActionResult> Overtime([FromQuery] DateTime? week, CancellationToken ct)
        => Ok(await _predictions.GetOvertimeAsync(week, ct));

    /// <summary>Rafraîchit la fenêtre glissante (12 semaines par défaut) et régénère les prévisions.</summary>
    [RequireAdmin]
    [HttpPost("refresh")]
    public IActionResult Refresh() => Accepted(new { message = _predictions.Schedule(PredictionJob.Refresh) });

    /// <summary>Reconstruit tout l'historique (premier déploiement), réentraîne et régénère.</summary>
    [RequireAdmin]
    [HttpPost("backfill")]
    public IActionResult Backfill() => Accepted(new { message = _predictions.Schedule(PredictionJob.Backfill) });

    /// <summary>Réentraîne les modèles actifs.</summary>
    [RequireAdmin]
    [HttpPost("train")]
    public IActionResult Train() => Accepted(new { message = _predictions.Schedule(PredictionJob.Train) });
}
