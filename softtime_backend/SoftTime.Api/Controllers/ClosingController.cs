using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using SoftTime.Api.Responses;
using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Clôture de paie assistée, en LECTURE SEULE : aucune route d'écriture (POST, PUT, PATCH, DELETE
/// répondent 405). Contrôles dans le même ordre que <see cref="PredictionsController"/> : la base
/// active (TenantMiddleware puis <see cref="ITenantConnectionService.EnsureAuthorizedAsync"/>), puis
/// la lecture fine : administrateur, ou porte 6 ET permission <c>REPORTS:CLOSING</c> effectives.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/closing")]
[Tags("Clôture")]
public class ClosingController : ApiControllerBase
{
    public const string Permission = "REPORTS:CLOSING";
    public const string Desactivee = "La clôture de paie assistée est désactivée.";

    private readonly IClosingService _closing;
    private readonly IClosingPresentationService _presentation;
    private readonly ClosingOptions _reglages;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;
    private readonly ILogger<ClosingController> _logger;

    public ClosingController(
        IClosingService closing,
        IClosingPresentationService presentation,
        IOptions<ClosingOptions> reglages,
        ICurrentUser user,
        IEffectiveRightsService rights,
        ILogger<ClosingController> logger)
    {
        _closing = closing;
        _presentation = presentation;
        _reglages = reglages.Value;
        _user = user;
        _rights = rights;
        _logger = logger;
    }

    /// <summary>
    /// Analyse de clôture d'une période (mois civil précédent sans dates) : statut, score, formule,
    /// comptages, départements, page d'anomalies (50 par défaut, 200 au plus) et comparaison si demandée.
    /// La liste omet les anomalies « Information » sauf <c>includeInformation=true</c> ; les comptages les incluent toujours.
    /// <c>reuseCache=true</c> (changement de page seulement) réutilise, jusqu'à 60 s, l'analyse déjà calculée pour la même période.
    /// </summary>
    [HttpGet("analysis")]
    public async Task<IActionResult> AnalysisAsync([FromQuery] ClosingAnalysisQuery query, CancellationToken ct)
    {
        await FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, Permission, ct);
        if (!_reglages.Enabled)
            return ApiErrors.Result(StatusCodes.Status404NotFound, Desactivee);

        var request = new ClosingAnalysisRequest(
            Jour(query.From), Jour(query.To), query.Departement, query.Page, query.PageSize, Options: null,
            IncludeComparison: query.IncludeComparison == true, IncludeInformation: query.IncludeInformation == true,
            ReuseCached: query.ReuseCache == true);
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
