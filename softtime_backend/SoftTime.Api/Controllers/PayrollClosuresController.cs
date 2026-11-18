using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Payroll;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Clôture de paie de la base active. Une seule écriture : POST (clôturer), définitive. Il n'existe AUCUNE route
/// d'annulation, de déblocage ni de suppression (PUT, PATCH, DELETE répondent 405) : une correction se fait sur la
/// période suivante. Clôturer exige la permission SAGE:{base}:CLOTURE (porte Bases), accordée explicitement, ou
/// l'administrateur ; vérifiée par le service.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/payroll-closures")]
[Tags("PayrollClosures")]
public class PayrollClosuresController : ApiControllerBase
{
    private readonly IPayrollClosureService _svc;

    public PayrollClosuresController(IPayrollClosureService svc) => _svc = svc;

    /// <summary>Clôture la période exacte d'un envoi. Refus 409 avec un message explicite si une condition manque.</summary>
    [HttpPost]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> CloseAsync([FromBody] PayrollClosureRequest request, CancellationToken ct)
        => Ok(await _svc.CloseAsync(request, ct));

    /// <summary>Périodes envoyées de la base active avec leur état (envoyée, clôturée) et le droit de clôturer de l'appelant.</summary>
    [HttpGet("periods")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> PeriodsAsync(CancellationToken ct) => Ok(await _svc.ListPeriodsAsync(ct));

    /// <summary>Périodes clôturées qui recoupent une sélection (bandeau des pages de modification).</summary>
    [HttpGet("state")]
    [RequireAnyDroit(Droit.Databases, Droit.Parameters, Droit.Processing, Droit.Other, Droit.Reports)]
    public async Task<IActionResult> StateAsync([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Ok(await _svc.StateAsync(from, to, ct));
}
