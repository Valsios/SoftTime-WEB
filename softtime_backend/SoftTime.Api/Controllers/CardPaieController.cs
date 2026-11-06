using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cardpaie")]
[Tags("CardPaie")]
public class CardPaieController : ControllerBase
{
    private readonly ICatalogService _svc;
    public CardPaieController(ICatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCardsAsync(ct));

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCardsPagedAsync(page, pageSize, search, ct));

    [HttpPost]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> Create([FromBody] CardPaieDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCardAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> Update(int id, [FromBody] CardPaieDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCardAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteCardAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Supprime la correspondance SAGE ↔ badge identifiée par le couple de matricules
    /// (portage de <c>SupprimerParCorrespondance</c> du desktop).
    /// </summary>
    [HttpDelete("by-correspondence")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> DeleteByCorrespondence(
        [FromQuery] string matriculeSage,
        [FromQuery] string? matriculePointeuse,
        CancellationToken ct)
    {
        await _svc.DeleteCardByCorrespondenceAsync(matriculeSage, matriculePointeuse, ct);
        return NoContent();
    }

    [HttpPost("auto-map")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> AutoMap(CancellationToken ct)
        => Ok(await _svc.AutoMapCardsAsync(ct));

    [HttpPost("import-csv")]
    [RequireDroit(Droit.Parameters)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCsv(IFormFile file, CancellationToken ct)
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
