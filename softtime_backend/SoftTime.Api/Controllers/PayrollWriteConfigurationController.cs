using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/payroll-write-config")]
[Tags("PayrollWriteConfiguration")]
public class PayrollWriteConfigurationController : ControllerBase
{
    private readonly IPayrollWriteConfigurationService _svc;
    public PayrollWriteConfigurationController(IPayrollWriteConfigurationService svc) => _svc = svc;

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
