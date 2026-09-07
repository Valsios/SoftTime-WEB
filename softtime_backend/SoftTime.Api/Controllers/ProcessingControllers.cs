using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/shifts")]
[Tags("Shifts")]
public class ShiftsController : ControllerBase
{
    private readonly PlanningService _svc;
    public ShiftsController(PlanningService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _svc.ListShiftsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShiftDto dto, CancellationToken ct)
        => Ok(await _svc.SaveShiftAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] ShiftDto dto, CancellationToken ct)
        => Ok(await _svc.SaveShiftAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _svc.DeleteShiftAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportShiftsExcelAsync(ExcelHelper.ReadFirstSheet(s), ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/employee-planning")]
[Tags("EmployeePlanning")]
public class EmployeePlanningController : ControllerBase
{
    private readonly PlanningService _svc;
    public EmployeePlanningController(PlanningService svc) => _svc = svc;

    [HttpGet("employees")]
    public async Task<IActionResult> Employees(CancellationToken ct)
        => Ok(await _svc.ListEmployeesAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? cardId,
        CancellationToken ct)
        => Ok(await _svc.ListEmployeePlanningAsync(from, to, cardId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] List<EmployeePlanningDto> rows, CancellationToken ct)
    {
        await _svc.SaveEmployeePlanningAsync(rows, ct);
        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] List<EmployeePlanningDto> rows, CancellationToken ct)
    {
        await _svc.SaveEmployeePlanningAsync(rows, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteEmployeePlanningAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var rows = ExcelHelper.ReadFirstSheet(stream);
        var headers = rows.Count > 0 ? rows[0].Keys.ToList() : new List<string>();
        if (headers.Count == 0)
            return BadRequest("Fichier Excel vide ou sans en-têtes.");
        return Ok(await _svc.ImportExcelAsync(rows, headers, ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/punches")]
[Tags("Punches")]
public class PunchesController : ControllerBase
{
    private readonly TimekeepingService _svc;
    public PunchesController(TimekeepingService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListPunchesAsync(filter, ct));

    [HttpPost("import-clock")]
    public async Task<IActionResult> ImportClock([FromBody] ImportPunchesRequest request, CancellationToken ct)
        => Ok(await _svc.ImportFromClockAsync(request, ct));

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportPunchesExcelAsync(ExcelHelper.ReadFirstSheet(s), ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/anomalies")]
[Tags("Anomalies")]
public class AnomaliesController : ControllerBase
{
    private readonly TimekeepingService _svc;
    public AnomaliesController(TimekeepingService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListAnomaliesAsync(filter, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _svc.DeleteAnomalyAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/corrected-hours")]
[Tags("CorrectedHours")]
public class CorrectedHoursController : ControllerBase
{
    private readonly TimekeepingService _svc;
    public CorrectedHoursController(TimekeepingService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PeriodRequest filter, CancellationToken ct)
        => Ok(await _svc.ListCorrectionsAsync(filter, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CorrectedHourDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCorrectionAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(decimal id, [FromBody] CorrectedHourDto dto, CancellationToken ct)
        => Ok(await _svc.SaveCorrectionAsync(dto with { Id = id }, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(decimal id, CancellationToken ct)
    {
        await _svc.DeleteCorrectionAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(await _svc.ImportCorrectionsExcelAsync(ExcelHelper.ReadFirstSheet(s), ct));
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/weekly-validation")]
[Tags("WeeklyValidation")]
public class WeeklyValidationController : ControllerBase
{
    private readonly OvertimeService _svc;
    public WeeklyValidationController(OvertimeService svc) => _svc = svc;

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.PreviewWeekAsync(request, ct));

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] WeeklyValidationRequest request, CancellationToken ct)
    {
        await _svc.ValidateWeekAsync(request, true, ct);
        return NoContent();
    }

    [HttpPost("unvalidate")]
    public async Task<IActionResult> Unvalidate([FromBody] WeeklyValidationRequest request, CancellationToken ct)
    {
        await _svc.ValidateWeekAsync(request, false, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[RequireDroit(Droit.Processing)]
[Route("api/overtime")]
[Tags("Overtime")]
public class OvertimeController : ControllerBase
{
    private readonly OvertimeService _svc;
    public OvertimeController(OvertimeService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string? matriculeFrom,
        [FromQuery] string? matriculeTo,
        [FromQuery] string? branche,
        CancellationToken ct)
        => Ok(await _svc.ListExoAsync(new PeriodRequest(matriculeFrom, matriculeTo, from, to, branche), ct));

    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate([FromBody] PeriodRequest request, CancellationToken ct)
        => Ok(await _svc.CalculateExoImpoAsync(request, ct));

    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromBody] PeriodRequest request, CancellationToken ct)
    {
        await _svc.PurgeHsAsync(request, ct);
        return NoContent();
    }

    [HttpPost("sync-sage")]
    public async Task<IActionResult> SyncSage([FromBody] PeriodRequest request, CancellationToken ct)
    {
        await _svc.SyncSageAsync(request, ct);
        return NoContent();
    }
}
