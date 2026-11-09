using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Employees;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/departement-service")]
[Tags("DepartementService")]
public class DepartementServiceController : ApiControllerBase
{
    private readonly IDepartementServiceLookup _svc;
    public DepartementServiceController(IDepartementServiceLookup svc) => _svc = svc;

    [HttpGet("lookup/{matricule}")]
    public async Task<IActionResult> LookupAsync(string matricule, CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceAsync(matricule, ct));

    [HttpPost("lookup-batch")]
    public async Task<IActionResult> LookupBatchAsync([FromBody] string[] matricules, CancellationToken ct)
        => Ok(await _svc.GetDepartementServiceBatchAsync(matricules ?? Array.Empty<string>(), ct));
}
