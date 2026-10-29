using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
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
    private readonly AuthService _auth;
    private readonly ICurrentUser _user;

    public SageDatabasesController(CatalogService svc, AuthService auth, ICurrentUser user)
    {
        _svc = svc;
        _auth = auth;
        _user = user;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _svc.ListSageAsync(ct);
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                list = list.Where(s => !string.IsNullOrEmpty(s.NomBd) && p.GrantedPermissions.Contains($"SAGE:{s.NomBd}:READ")).ToList();
            }
        }
        return Ok(list);
    }

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
    {
        IReadOnlyCollection<string>? allowed = null;
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
                allowed = DatabasePermissionNames.Read(p.GrantedPermissions, "SAGE");
        }
        return Ok(await _svc.ListSagePagedAsync(allowed, page, pageSize, search, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SageDbDto dto, CancellationToken ct)
    {
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                var canCreate = p.GrantedPermissions.Any(x => x.StartsWith("SAGE:") && x.EndsWith(":CREATE"))
                                || p.GrantedPermissions.Contains("SAGE:CREATE");
                if (!canCreate)
                    return StatusCode(StatusCodes.Status403Forbidden, "Vous n'avez pas la permission d'ajouter une base SAGE.");
            }
        }
        return Ok(await _svc.SaveSageAsync(dto with { Id = 0 }, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SageDbDto dto, CancellationToken ct)
    {
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                var existing = await _svc.GetSageByIdAsync(id, ct);
                var dbName = existing?.NomBd ?? dto.NomBd;
                var canUpdate = p.GrantedPermissions.Contains($"SAGE:{dbName}:UPDATE");
                if (!canUpdate)
                    return StatusCode(StatusCodes.Status403Forbidden, $"Vous n'avez pas la permission de modifier la base '{dbName}'.");
            }
        }
        return Ok(await _svc.SaveSageAsync(dto with { Id = id }, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                var existing = await _svc.GetSageByIdAsync(id, ct);
                var dbName = existing?.NomBd;
                if (!string.IsNullOrEmpty(dbName))
                {
                    var canDelete = p.GrantedPermissions.Contains($"SAGE:{dbName}:DELETE");
                    if (!canDelete)
                        return StatusCode(StatusCodes.Status403Forbidden, $"Vous n'avez pas la permission de supprimer la base '{dbName}'.");
                }
            }
        }
        await _svc.DeleteSageAsync(id, ct);
        return NoContent();
    }
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] TestSageConnectionDto dto, CancellationToken ct)
        => Ok(await _svc.TestSageConnectionAsync(dto, ct));

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
    private readonly CatalogService _svc;
    public PayrollWriteConfigurationController(CatalogService svc) => _svc = svc;

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
    private readonly CatalogService _svc;
    private readonly AuthService _auth;
    private readonly ICurrentUser _user;

    public PointeuseDatabasesController(CatalogService svc, AuthService auth, ICurrentUser user)
    {
        _svc = svc;
        _auth = auth;
        _user = user;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _svc.ListPointeuseAsync(ct);
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                list = list.Where(s => !string.IsNullOrEmpty(s.NomBd) && p.GrantedPermissions.Contains($"POINTEUSE:{s.NomBd}:READ")).ToList();
            }
        }
        return Ok(list);
    }

    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct)
    {
        IReadOnlyCollection<string>? allowed = null;
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
                allowed = DatabasePermissionNames.Read(p.GrantedPermissions, "POINTEUSE");
        }
        return Ok(await _svc.ListPointeusePagedAsync(allowed, page, pageSize, search, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PointeuseDbDto dto, CancellationToken ct)
    {
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                var canCreate = p.GrantedPermissions.Any(x => x.StartsWith("POINTEUSE:") && x.EndsWith(":CREATE"))
                                || p.GrantedPermissions.Contains("POINTEUSE:CREATE");
                if (!canCreate)
                    return StatusCode(StatusCodes.Status403Forbidden, "Vous n'avez pas la permission d'ajouter une base pointeuse.");
            }
        }
        return Ok(await _svc.SavePointeuseAsync(dto with { Id = 0 }, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PointeuseDbDto dto, CancellationToken ct)
    {
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                var existing = await _svc.GetPointeuseByIdAsync(id, ct);
                var dbName = existing?.NomBd ?? dto.NomBd;
                var canUpdate = p.GrantedPermissions.Contains($"POINTEUSE:{dbName}:UPDATE");
                if (!canUpdate)
                    return StatusCode(StatusCodes.Status403Forbidden, $"Vous n'avez pas la permission de modifier la base pointeuse '{dbName}'.");
            }
        }
        return Ok(await _svc.SavePointeuseAsync(dto with { Id = id }, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (_user.UserId is decimal uid)
        {
            var p = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
            if (p.IsCustomized)
            {
                var existing = await _svc.GetPointeuseByIdAsync(id, ct);
                var dbName = existing?.NomBd;
                if (!string.IsNullOrEmpty(dbName))
                {
                    var canDelete = p.GrantedPermissions.Contains($"POINTEUSE:{dbName}:DELETE");
                    if (!canDelete)
                        return StatusCode(StatusCodes.Status403Forbidden, $"Vous n'avez pas la permission de supprimer la base pointeuse '{dbName}'.");
                }
            }
        }
        await _svc.DeletePointeuseAsync(id, ct);
        return NoContent();
    }
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] TestPointeuseConnectionDto dto, CancellationToken ct)
        => Ok(await _svc.TestPointeuseConnectionAsync(dto, ct));
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
[RequireDroit(Droit.Parameters)]
[Route("api/departement-service")]
[Tags("DepartementService")]
public class DepartementServiceController : ControllerBase
{
    private readonly CatalogService _svc;
    public DepartementServiceController(CatalogService svc) => _svc = svc;

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
    private readonly CatalogService _svc;
    public CardPaieController(CatalogService svc) => _svc = svc;

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
    private readonly CatalogService _svc;
    public AffectationsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? categoryId, CancellationToken ct)
        => Ok(await _svc.ListAffectationsAsync(categoryId, ct));

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

    [HttpGet("all")]
    public async Task<IActionResult> GetAllRows(
        [FromQuery] int? categoryId,
        [FromQuery] string? departement,
        [FromQuery] string? service,
        CancellationToken ct)
        => Ok(await _svc.ListAllAffectationsAsync(categoryId, departement, service, ct));

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
    private readonly CatalogService _svc;
    public HolidaysController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListHolidaysAsync(ct));

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
    private readonly CatalogService _svc;
    public MajorationsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListMajorationsAsync(ct));

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
    private readonly CatalogService _svc;
    public AbsenceCodesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListAbsenceCodesAsync(ct));

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
    private readonly CatalogService _svc;
    public CodeConstantesController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListCodeConstantesAsync(ct));

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

/// <summary>
/// Extrait les noms de bases autorisées à partir des permissions détaillées
/// (<c>SAGE:{nom}:READ</c> / <c>POINTEUSE:{nom}:READ</c>).
/// </summary>
internal static class DatabasePermissionNames
{
    public static IReadOnlyCollection<string> Read(IEnumerable<string> permissions, string system)
    {
        var prefix = system + ":";
        return permissions
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && p.EndsWith(":READ", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Split(':'))
            .Where(parts => parts.Length == 3)
            .Select(parts => parts[1])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
