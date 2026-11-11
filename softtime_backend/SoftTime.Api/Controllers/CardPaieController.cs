using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Security;
using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/card-paie")]
[Tags("CardPaie")]
public class CardPaieController : ApiControllerBase
{
    private readonly ICardPaieService _svc;
    public CardPaieController(ICardPaieService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListCardsAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCardsPagedAsync(page, pageSize, search, ct));

    [HasPermission(Permissions.CardPaie.Create)]
    [HttpPost]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> CreateAsync([FromBody] CardPaieDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCardAsync(dto with { Id = 0 }, ct));

    [HasPermission(Permissions.CardPaie.Update)]
    [HttpPut("{id:int}")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] CardPaieDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCardAsync(dto with { Id = id }, ct));

    [HasPermission(Permissions.CardPaie.Delete)]
    [HttpDelete("{id:int}")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct)
    {
        await _svc.DeleteCardAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Supprime la correspondance SAGE ↔ badge identifiée par le couple de matricules
    /// (portage de <c>SupprimerParCorrespondance</c> du desktop).
    /// </summary>
    [HasPermission(Permissions.CardPaie.Delete)]
    [HttpDelete("by-correspondence")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> DeleteByCorrespondenceAsync(
        [FromQuery] string matriculeSage,
        [FromQuery] string? matriculePointeuse,
        CancellationToken ct)
    {
        await _svc.DeleteCardByCorrespondenceAsync(matriculeSage, matriculePointeuse, ct);
        return NoContent();
    }

    [HasPermission(Permissions.CardPaie.Create)]
    [HttpPost("auto-map")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> AutoMapAsync(CancellationToken ct)
        => Ok(await _svc.AutoMapCardsAsync(ct));

    [HasPermission(Permissions.CardPaie.Create)]
    [HttpPost("import-csv")]
    [RequireDroit(Droit.Parameters)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCsvAsync(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync(ct);
        var lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l =>
            {
                var sep = l.Contains(';') ? ';' : ',';
                return l.Split(sep).Select(p => p.Trim().Trim('"')).ToArray();
            })
            .ToList();
        return Ok(await _svc.ImportCorrespondencesCsvAsync(lines, ct));
    }
}
