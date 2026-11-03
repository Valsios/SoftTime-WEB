using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sage-databases")]
[Tags("SageDatabases")]
public class SageDatabasesController : ControllerBase
{
    private readonly ICatalogService _svc;
    private readonly IDatabaseListingService _listing;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;

    public SageDatabasesController(ICatalogService svc, IDatabaseListingService listing, IDatabaseConfigurationAuthorizer configAuth)
    {
        _svc = svc;
        _listing = listing;
        _configAuth = configAuth;
    }

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _listing.ListSagePagedAsync(page, pageSize, search, ct));

    [HttpPost]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Create([FromBody] SageDbDto dto, CancellationToken ct)
        => Ok(await _svc.SaveSageAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Update(int id, [FromBody] SageDbDto dto, CancellationToken ct)
        => Ok(await _svc.SaveSageAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteSageAsync(id, ct);
        return NoContent();
    }

    [HttpPost("test-connection")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> TestConnection([FromBody] TestSageConnectionDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(DatabaseKind.Sage, ct);
        return Ok(await _svc.TestSageConnectionAsync(dto, ct));
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(CancellationToken ct)
        => Ok(await _svc.ActivateSagePairAsync(ct));

    [HttpGet("lookup/{matricule}")]
    public async Task<IActionResult> Lookup(string matricule, CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceAsync(matricule, ct));

    [HttpPost("lookup-batch")]
    public async Task<IActionResult> LookupBatch([FromBody] string[] matricules, CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceBatchAsync(matricules ?? Array.Empty<string>(), ct));

    [HttpGet("departement-service-options")]
    public async Task<IActionResult> DepartementServiceOptions(CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceOptionsAsync(ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/payroll-write-config")]
[Tags("PayrollWriteConfiguration")]
public class PayrollWriteConfigurationController : ControllerBase
{
    private readonly ICatalogService _svc;
    public PayrollWriteConfigurationController(ICatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListPayrollWriteConfigurationsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PayrollWriteConfigurationDto dto, CancellationToken ct)
        => Ok(await _svc.SavePayrollWriteConfigurationAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PayrollWriteConfigurationDto dto, CancellationToken ct)
        => Ok(await _svc.SavePayrollWriteConfigurationAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeletePayrollWriteConfigurationAsync(id, ct);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct) => Ok(await _svc.TestPayrollWriteConfigurationAsync(ct));
}

[ApiController]
[Authorize]
[Route("api/pointeuse-databases")]
[Tags("PointeuseDatabases")]
public class PointeuseDatabasesController : ControllerBase
{
    private readonly ICatalogService _svc;
    private readonly IDatabaseListingService _listing;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;

    public PointeuseDatabasesController(ICatalogService svc, IDatabaseListingService listing, IDatabaseConfigurationAuthorizer configAuth)
    {
        _svc = svc;
        _listing = listing;
        _configAuth = configAuth;
    }

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _listing.ListPointeusePagedAsync(page, pageSize, search, ct));

    [HttpPost]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Create([FromBody] PointeuseDbDto dto, CancellationToken ct)
        => Ok(await _svc.SavePointeuseAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Update(int id, [FromBody] PointeuseDbDto dto, CancellationToken ct)
        => Ok(await _svc.SavePointeuseAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeletePointeuseAsync(id, ct);
        return NoContent();
    }

    [HttpPost("test-connection")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> TestConnection([FromBody] TestPointeuseConnectionDto dto, CancellationToken ct)
    {
        await _configAuth.EnsureCanConfigureAnyAsync(DatabaseKind.Pointeuse, ct);
        return Ok(await _svc.TestPointeuseConnectionAsync(dto, ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/clock-params")]
[Tags("ClockParams")]
public class ClockParamsController : ControllerBase
{
    private readonly ICatalogService _svc;
    public ClockParamsController(ICatalogService svc) => _svc = svc;

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
    private readonly ICatalogService _svc;
    public CorrespondenceModeController(ICatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetCorrespondenceAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] CorrespondenceModeDto dto, CancellationToken ct)
        => Ok(await _svc.SetCorrespondenceAsync(dto.Active, ct));
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/departement-service")]
[Tags("DepartementService")]
public class DepartementServiceController : ControllerBase
{
    private readonly ICatalogService _svc;
    public DepartementServiceController(ICatalogService svc) => _svc = svc;

    [HttpGet("lookup/{matricule}")]
    public async Task<IActionResult> Lookup(string matricule, CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceAsync(matricule, ct));

    [HttpPost("lookup-batch")]
    public async Task<IActionResult> LookupBatch([FromBody] string[] matricules, CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceBatchAsync(matricules ?? Array.Empty<string>(), ct));
}

[ApiController]
[Authorize]
[Route("api/cardpaie")]
[Tags("CardPaie")]
public class CardPaieController : ControllerBase
{
    private readonly ICatalogService _svc;
    private readonly IImportFileReader _files;

    public CardPaieController(ICatalogService svc, IImportFileReader files)
    {
        _svc = svc;
        _files = files;
    }

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
        await using var stream = file.OpenReadStream();
        return Ok(await _svc.ImportCorrespondencesCsvAsync(await _files.ReadCsvLinesAsync(stream, ct), ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/categories")]
[Tags("Categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICatalogService _svc;
    public CategoriesController(ICatalogService svc) => _svc = svc;

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCategoriesPagedAsync(page, pageSize, search, ct));

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
    private readonly ICatalogService _svc;
    private readonly IImportFileReader _files;

    public AffectationsController(ICatalogService svc, IImportFileReader files)
    {
        _svc = svc;
        _files = files;
    }

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? categoryId,
        [FromQuery] string? departement,
        [FromQuery] string? service,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken ct)
        => Ok(await _svc.ListAffectationsPagedAsync(categoryId, departement, service, page, pageSize, search, ct));

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions(CancellationToken ct)
        => Ok(await _svc.GetAffectationOptionsAsync(ct));

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
        return Ok(await _svc.ImportAffectationsExcelAsync(_files.ReadExcelRows(s), ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/tolerances")]
[Tags("Tolerances")]
public class TolerancesController : ControllerBase
{
    private readonly ICatalogService _svc;
    public TolerancesController(ICatalogService svc) => _svc = svc;

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? categoryId, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListTolerancesPagedAsync(categoryId, page, pageSize, search, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ToleranceDto dto, CancellationToken ct)
        => Ok(await _svc.SaveToleranceAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteToleranceAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/holidays")]
[Tags("Holidays")]
public class HolidaysController : ControllerBase
{
    private readonly ICatalogService _svc;
    public HolidaysController(ICatalogService svc) => _svc = svc;

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListHolidaysPagedAsync(page, pageSize, search, ct));

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
    private readonly ICatalogService _svc;
    public MajorationsController(ICatalogService svc) => _svc = svc;

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListMajorationsPagedAsync(page, pageSize, search, ct));

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
    private readonly ICatalogService _svc;
    public AbsenceCodesController(ICatalogService svc) => _svc = svc;

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListAbsenceCodesPagedAsync(page, pageSize, search, ct));

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
    private readonly ICatalogService _svc;
    public CodeConstantesController(ICatalogService svc) => _svc = svc;

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _svc.ListCodeConstantesPagedAsync(page, pageSize, search, ct));

    [HttpGet("sage-options")]
    public async Task<IActionResult> SageOptions(CancellationToken ct) => Ok(await _svc.ListSageConstantOptionsAsync(ct));

    [HttpGet("autre-tables")]
    public async Task<IActionResult> OtherTables(CancellationToken ct) => Ok(await _svc.ListOtherCodeConstantTablesAsync(ct));

    [HttpGet("autre-values")]
    public async Task<IActionResult> OtherValues([FromQuery] string table, CancellationToken ct)
        => Ok(await _svc.ListOtherCodeConstantValuesAsync(table, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CodeConstanteDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCodeConstanteAsync(dto with { Id = id }, ct));
}
