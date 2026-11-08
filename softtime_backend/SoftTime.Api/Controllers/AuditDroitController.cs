using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[Route("api/v1/privileges/audit")]
[Tags("Privileges")]
public class AuditDroitController : ControllerBase
{
    private readonly IRightsAuditService _auth;
    public AuditDroitController(IRightsAuditService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? typeCible,
        [FromQuery] decimal? idCible,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _auth.GetAuditDroitsAsync(typeCible, idCible, from, to, ct));
}
