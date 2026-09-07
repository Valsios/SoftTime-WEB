using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sage-databases")]
[Tags("SageDatabases")]
public class SageDatabasesController : ControllerBase
{
    private readonly CatalogService _svc;
    public SageDatabasesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListSageAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SageDbDto dto, CancellationToken ct)
        => Ok(await _svc.SaveSageAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SageDbDto dto, CancellationToken ct)
        => Ok(await _svc.SaveSageAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteSageAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/pointeuse-databases")]
[Tags("PointeuseDatabases")]
public class PointeuseDatabasesController : ControllerBase
{
    private readonly CatalogService _svc;
    public PointeuseDatabasesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListPointeuseAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PointeuseDbDto dto, CancellationToken ct)
        => Ok(await _svc.SavePointeuseAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PointeuseDbDto dto, CancellationToken ct)
        => Ok(await _svc.SavePointeuseAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeletePointeuseAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/clock-params")]
[Tags("ClockParams")]
public class ClockParamsController : ControllerBase
{
    private readonly CatalogService _svc;
    public ClockParamsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetClockAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ClockParamDto dto, CancellationToken ct)
        => Ok(await _svc.SaveClockAsync(dto, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/correspondence-mode")]
[Tags("CorrespondenceMode")]
public class CorrespondenceModeController : ControllerBase
{
    private readonly CatalogService _svc;
    public CorrespondenceModeController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetCorrespondenceAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] CorrespondenceModeDto dto, CancellationToken ct)
        => Ok(await _svc.SetCorrespondenceAsync(dto.Active, ct));
}

[ApiController]
[Authorize]
[Route("api/cardpaie")]
[Tags("CardPaie")]
public class CardPaieController : ControllerBase
{
    private readonly CatalogService _svc;
    public CardPaieController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCardsAsync(ct));

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

    [HttpPost("auto-map")]
    [RequireDroit(Droit.Parameters)]
    public async Task<IActionResult> AutoMap(CancellationToken ct)
        => Ok(new { added = await _svc.AutoMapCardsAsync(ct) });

    [HttpPost("import-csv")]
    [RequireDroit(Droit.Parameters)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCsv(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync(ct);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l =>
            {
                var sep = l.Contains(';') ? ';' : ',';
                return l.Split(sep).Select(p => p.Trim().Trim('"')).ToArray();
            })
            .ToList();
        return Ok(await _svc.ImportCorrespondencesCsvAsync(lines, ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/categories")]
[Tags("Categories")]
public class CategoriesController : ControllerBase
{
    private readonly CatalogService _svc;
    public CategoriesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCategoriesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCategoryAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCategoryAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteCategoryAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/affectations")]
[Tags("Affectations")]
public class AffectationsController : ControllerBase
{
    private readonly CatalogService _svc;
    public AffectationsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? categoryId, CancellationToken ct)
        => Ok(await _svc.ListAffectationsAsync(categoryId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AffectationDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAffectationAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AffectationDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAffectationAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteAffectationAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportAffectationsExcelAsync(ExcelHelper.ReadFirstSheet(s), ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/tolerances")]
[Tags("Tolerances")]
public class TolerancesController : ControllerBase
{
    private readonly CatalogService _svc;
    public TolerancesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? categoryId, CancellationToken ct)
        => Ok(await _svc.ListTolerancesAsync(categoryId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = id }, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/holidays")]
[Tags("Holidays")]
public class HolidaysController : ControllerBase
{
    private readonly CatalogService _svc;
    public HolidaysController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListHolidaysAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HolidayDto dto, CancellationToken ct)
        => Ok(await _svc.SaveHolidayAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] HolidayDto dto, CancellationToken ct)
        => Ok(await _svc.SaveHolidayAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteHolidayAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-sage")]
    public async Task<IActionResult> ImportSage(CancellationToken ct)
    {
        await _svc.ImportSageHolidaysAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/majorations")]
[Tags("Majorations")]
public class MajorationsController : ControllerBase
{
    private readonly CatalogService _svc;
    public MajorationsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListMajorationsAsync(ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MajorationDto dto, CancellationToken ct)
        => Ok(await _svc.SaveMajorationAsync(dto with { Id = id }, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/absence-codes")]
[Tags("AbsenceCodes")]
public class AbsenceCodesController : ControllerBase
{
    private readonly CatalogService _svc;
    public AbsenceCodesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListAbsenceCodesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenceCodeDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAbsenceCodeAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenceCodeDto dto, CancellationToken ct)
        => Ok(await _svc.SaveAbsenceCodeAsync(dto with { Id = id }, ct));

    [HttpPost("sync-sage")]
    public async Task<IActionResult> SyncSage(CancellationToken ct)
    {
        await _svc.SyncAbsenceCodesFromSageAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/code-constantes")]
[Tags("CodeConstantes")]
public class CodeConstantesController : ControllerBase
{
    private readonly CatalogService _svc;
    public CodeConstantesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCodeConstantesAsync(ct));

    [HttpGet("sage-options")]
    public async Task<IActionResult> SageOptions(CancellationToken ct) => Ok(await _svc.ListSageConstantOptionsAsync(ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CodeConstanteDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCodeConstanteAsync(dto with { Id = id }, ct));
}
