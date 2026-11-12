using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Security;
using SoftTime.Application.Abstractions.Permissions;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Traceability)]
[HasPermission(Permissions.Audit.Read, Permissions.Privileges.Read)]
[Route("api/v1/privileges/audit")]
[Tags("Privileges")]
public class AuditDroitController : ApiControllerBase
{
    private readonly IRightsAuditService _auth;
    public AuditDroitController(IRightsAuditService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? typeCible,
        [FromQuery] decimal? idCible,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _auth.GetAuditDroitsAsync(typeCible, idCible, from, to, ct));
}
