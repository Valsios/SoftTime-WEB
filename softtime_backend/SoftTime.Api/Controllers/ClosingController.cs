using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Clôture de paie assistée, en LECTURE SEULE : aucune route d'écriture (POST, PUT, PATCH, DELETE
/// répondent 405). La base active est contrôlée par le TenantMiddleware ; la lecture fine
/// (administrateur, ou porte 6 ET permission <c>REPORTS:CLOSING</c>) par <see cref="IClosingReportService"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/closing")]
[Tags("Clôture")]
public class ClosingController : ControllerBase
{
    private readonly IClosingReportService _closing;

    public ClosingController(IClosingReportService closing) => _closing = closing;

    /// <summary>
    /// Analyse de clôture d'une période (mois civil précédent sans dates) : statut, score, formule,
    /// comptages, départements, page d'anomalies (50 par défaut, 200 au plus) et comparaison si demandée.
    /// La liste omet les anomalies « Information » sauf <c>includeInformation=true</c> ; les comptages les incluent toujours.
    /// 404 si la fonctionnalité est désactivée.
    /// </summary>
    [HttpGet("analysis")]
    public async Task<IActionResult> Analysis([FromQuery] ClosingAnalysisQuery query, CancellationToken ct)
        => Ok(await _closing.AnalyseAsync(query, ct));
}
