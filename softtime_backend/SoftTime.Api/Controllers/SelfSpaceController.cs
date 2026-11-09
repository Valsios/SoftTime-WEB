using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using SoftTime.Api.Authorization;
using SoftTime.Api.Responses;
using SoftTime.Application.Abstractions.SelfService;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

/// <summary>
/// « Mon espace » : les données du salarié connecté, en lecture seule (GET uniquement ; toute autre méthode répond
/// 405). Aucune porte n'est requise : l'accès repose sur la permission SELF:READ et sur le matricule du jeton (voir
/// <see cref="ISelfServiceGuard"/>). Aucun paramètre de route, de requête ou d'en-tête ne désigne un salarié : seule la
/// période (from, to) est acceptée. Désactivé par défaut (SelfService:Enabled) : 404 tant que la fonction est coupée.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/me/space")]
[Tags("MonEspace")]
[NoStore]
public class SelfSpaceController : ApiControllerBase
{
    public const string Desactivee = "Mon espace n'est pas activé sur ce serveur.";

    private readonly ISelfSpaceService _space;
    private readonly SelfServiceOptions _options;

    public SelfSpaceController(ISelfSpaceService space, IOptions<SelfServiceOptions> options)
    {
        _space = space;
        _options = options.Value;
    }

    /// <summary>Synthèse de la période (mois en cours par défaut).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> SummaryAsync([FromQuery] SelfPeriodQuery query, CancellationToken ct)
        => _options.Enabled ? Ok(await _space.SummaryAsync(query, ct)) : DesactiveeResult();

    /// <summary>Semaines validées (lundi → dimanche), heures supplémentaires 130 % / 150 % et cumuls EXO /
    /// IMPO.</summary>
    [HttpGet("weeks")]
    public async Task<IActionResult> WeeksAsync([FromQuery] SelfPeriodQuery query, CancellationToken ct)
        => _options.Enabled ? Ok(await _space.WeeksAsync(query, ct)) : DesactiveeResult();

    /// <summary>Anomalies du jour : absences sans pointage et pointages à vérifier.</summary>
    [HttpGet("anomalies")]
    public async Task<IActionResult> AnomaliesAsync([FromQuery] SelfPeriodQuery query, CancellationToken ct)
        => _options.Enabled ? Ok(await _space.AnomaliesAsync(query, ct)) : DesactiveeResult();

    /// <summary>Absences et congés (congés lus dans SAGE).</summary>
    [HttpGet("absences")]
    public async Task<IActionResult> AbsencesAsync([FromQuery] SelfPeriodQuery query, CancellationToken ct)
        => _options.Enabled ? Ok(await _space.AbsencesAsync(query, ct)) : DesactiveeResult();

    /// <summary>Retards : entrée retenue, heure prévue, tolérance et minutes de retard.</summary>
    [HttpGet("delays")]
    public async Task<IActionResult> DelaysAsync([FromQuery] SelfPeriodQuery query, CancellationToken ct)
        => _options.Enabled ? Ok(await _space.DelaysAsync(query, ct)) : DesactiveeResult();

    private static ObjectResult DesactiveeResult() => ApiErrors.Result(StatusCodes.Status404NotFound, Desactivee);
}
