using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[Route("api/privileges/audit")]
[Tags("Privileges")]
public class AuditDroitController : ControllerBase
{
    private readonly AuthService _auth;
    public AuditDroitController(AuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? typeCible,
        [FromQuery] decimal? idCible,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _auth.GetAuditDroitsAsync(typeCible, idCible, from, to, ct));
}
