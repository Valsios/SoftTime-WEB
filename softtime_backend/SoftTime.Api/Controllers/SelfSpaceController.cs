using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.SelfService;

namespace SoftTime.Api.Controllers;

/// <summary>
/// « Mon espace » : les données du salarié connecté, en lecture seule (GET uniquement ; toute autre méthode répond 405).
/// Aucune porte n'est requise : l'accès repose sur la permission SELF:READ et sur le matricule du jeton (voir
/// <see cref="SelfServiceGuard"/>). Aucun paramètre de route, de requête ou d'en-tête ne désigne un salarié : seules la période
/// (from, to) est acceptée. Désactivé par défaut (SelfService:Enabled) : 404 tant que la fonction est coupée.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/space")]
[Tags("MonEspace")]
public class SelfSpaceController : ControllerBase
{
    private readonly SelfSpaceService _space;
    private readonly SelfServiceGuard _guard;

    public SelfSpaceController(SelfSpaceService space, SelfServiceGuard guard)
    {
        _space = space;
        _guard = guard;
    }

    private IActionResult Desactivee() => NotFound(new { error = "Mon espace n'est pas activé sur ce serveur." });

    /// <summary>Synthèse de la période (mois en cours par défaut).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => !_guard.Enabled ? Desactivee() : Ok(await _space.SummaryAsync(from, to, ct));

    /// <summary>Semaines validées (lundi → dimanche), heures supplémentaires 130 % / 150 % et cumuls EXO / IMPO.</summary>
    [HttpGet("weeks")]
    public async Task<IActionResult> Weeks([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => !_guard.Enabled ? Desactivee() : Ok(await _space.WeeksAsync(from, to, ct));

    [HttpGet("anomalies")]
    public async Task<IActionResult> Anomalies([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => !_guard.Enabled ? Desactivee() : Ok(await _space.AnomaliesAsync(from, to, ct));

    /// <summary>Absences et congés (congés lus dans SAGE).</summary>
    [HttpGet("absences")]
    public async Task<IActionResult> Absences([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => !_guard.Enabled ? Desactivee() : Ok(await _space.AbsencesAsync(from, to, ct));

    [HttpGet("delays")]
    public async Task<IActionResult> Delays([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => !_guard.Enabled ? Desactivee() : Ok(await _space.DelaysAsync(from, to, ct));
}
