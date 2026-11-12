using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
// Configuration de l'écriture dans la paie SAGE (connexion comprise) : aucune permission fine au catalogue et aucun
// écran ne l'utilise, elle est donc réservée à l'administrateur.
[RequireAdmin]
[Route("api/v1/payroll-write-config")]
[Tags("PayrollWriteConfiguration")]
public class PayrollWriteConfigurationController : ApiControllerBase
{
    private readonly IPayrollWriteConfigurationService _svc;
    public PayrollWriteConfigurationController(IPayrollWriteConfigurationService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct) => Ok(await _svc.ListPayrollWriteConfigurationsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] PayrollWriteConfigurationDto dto, CancellationToken ct)
        => Ok(await _svc.SavePayrollWriteConfigurationAsync(dto with { Id = 0 }, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] PayrollWriteConfigurationDto dto, CancellationToken ct)
        => Ok(await _svc.SavePayrollWriteConfigurationAsync(dto with { Id = id }, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct)
    {
        await _svc.DeletePayrollWriteConfigurationAsync(id, ct);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestAsync(CancellationToken ct) => Ok(await _svc.TestPayrollWriteConfigurationAsync(ct));
}
