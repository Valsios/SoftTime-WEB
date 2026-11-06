using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

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
