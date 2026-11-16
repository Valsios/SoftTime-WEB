using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Variables envoyées à la paie pour la base SAGE active. Lecture et enregistrement de la configuration : mêmes droits
/// que la modification d'une base dans l'écran Bases de données (porte Bases + permission SAGE:{base}:UPDATE, ou
/// administrateur), vérifiés côté serveur par le service.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/payroll-variables")]
[Tags("PayrollVariables")]
public class PayrollVariablesController : ApiControllerBase
{
    private readonly IPayrollVariableService _svc;

    public PayrollVariablesController(IPayrollVariableService svc) => _svc = svc;

    [HttpGet]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _svc.GetConfigurationAsync(ct));

    [HttpPut]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> SaveAsync([FromBody] List<PayrollVariableSaveDto> variables, CancellationToken ct)
        => Ok(await _svc.SaveAsync(variables ?? new List<PayrollVariableSaveDto>(), ct));

    /// <summary>Variables actives (noms seulement) : page Heures supplémentaires, Contrôle avant paie, écran Bases.</summary>
    [HttpGet("actives")]
    [RequireAnyDroit(Droit.Processing, Droit.Reports, Droit.Databases)]
    public async Task<IActionResult> GetActivesAsync(CancellationToken ct) => Ok(await _svc.GetActivesAsync(ct));
}
