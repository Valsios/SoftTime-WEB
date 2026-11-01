using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Clôture de paie assistée, en LECTURE SEULE : aucune route d'écriture (POST, PUT, PATCH, DELETE
/// répondent 405). Contrôles dans le même ordre que <see cref="PredictionsController"/> : la base
/// active (TenantMiddleware puis <see cref="TenantConnectionService.EnsureAuthorizedAsync"/>), puis
/// la lecture fine : administrateur, ou porte 6 ET permission <c>REPORTS:CLOSING</c> effectives.
/// </summary>
[ApiController]
[Authorize]
[Route("api/closing")]
[Tags("Clôture")]
public class ClosingController : ControllerBase
{
    public const string Permission = "REPORTS:CLOSING";
    public const string Desactivee = "La clôture de paie assistée est désactivée.";

    private readonly ClosingService _closing;
    private readonly ClosingPresentationService _presentation;
    private readonly ClosingOptions _reglages;
    private readonly ICurrentUser _user;
    private readonly EffectiveRightsService _rights;
    private readonly ILogger<ClosingController> _logger;

    public ClosingController(
        ClosingService closing,
        ClosingPresentationService presentation,
        ClosingOptions reglages,
        ICurrentUser user,
        EffectiveRightsService rights,
        ILogger<ClosingController> logger)
    {
        _closing = closing;
        _presentation = presentation;
        _reglages = reglages;
        _user = user;
        _rights = rights;
        _logger = logger;
    }

    /// <summary>
    /// Analyse de clôture d'une période (mois civil précédent sans dates) : statut, score, formule,
    /// comptages, départements, page d'anomalies (50 par défaut, 200 au plus) et comparaison si demandée.
    /// </summary>
    [HttpGet("analysis")]
    public async Task<IActionResult> Analysis(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? departement,
        [FromQuery] bool? includeComparison,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        await FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, Permission, ct);
        if (!_reglages.Enabled)
            return NotFound(new { error = Desactivee });

        var request = new ClosingAnalysisRequest(
            Jour(from), Jour(to), departement, page, pageSize, Options: null, IncludeComparison: includeComparison == true);
        var analyse = await _closing.AnalyserAsync(request, ct);
        var rapport = await _presentation.PresenterAsync(analyse, ct);

        // Aucun nom ni matricule dans le journal : période, volumes et statut seulement.
        _logger.LogInformation("Analyse de clôture {From:yyyy-MM-dd} → {To:yyyy-MM-dd} : {Nombre} anomalie(s), statut {Statut}.",
            rapport.From, rapport.To, rapport.NombreAnomalies, rapport.Statut ?? "aucun");
        return Ok(rapport);
    }

    /// <summary>Date du jour demandé, convertie en UTC si elle est horodatée en local (comme le journal).</summary>
    private static DateTime Jour(DateTime? valeur)
    {
        if (valeur is not DateTime date)
            return default;
        return (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date).Date;
    }
}
