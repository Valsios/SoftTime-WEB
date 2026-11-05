using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Consultation du journal central des modifications (<c>suivi_JOURNAL</c>), en lecture seule : aucune route
/// d'écriture, de modification ni de suppression. Le journal est dans la base de l'application : l'en-tête
/// X-Sage-Database n'est pas requis et le filtre sur la base SAGE reste facultatif. Accès contrôlé par
/// <see cref="JournalService"/> : administrateur, ou porte 5 ET permission TRACE:AUDIT effectives.
/// </summary>
[ApiController]
[Authorize]
[Route("api/journal")]
[Tags("Audit")]
public class JournalController : ControllerBase
{
    private readonly JournalService _journal;

    public JournalController(JournalService journal) => _journal = journal;

    /// <summary>Lignes du journal, les plus récentes d'abord (pagination SQL, 50 par défaut, 200 au plus).</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? user,
        [FromQuery] string? action,
        [FromQuery] string? table,
        [FromQuery] string? key,
        [FromQuery] Guid? correlation,
        [FromQuery] string? sageDatabase,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => Ok(await _journal.ListAsync(
            new JournalQuery(from, to, user, action, table, key, correlation, sageDatabase, page, pageSize), ct));

    /// <summary>Valeurs distinctes (tables, actions, utilisateurs) de la période, plafonnées.</summary>
    [HttpGet("facets")]
    public async Task<IActionResult> Facets([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _journal.GetFacetsAsync(from, to, ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => Ok(await _journal.GetAsync(id, ct));
}
