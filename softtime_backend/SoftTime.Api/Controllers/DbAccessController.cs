using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/db-access")]
[Tags("DbAccess")]
public class DbAccessController : ApiControllerBase
{
    private readonly IDatabaseAccessService _auth;
    public DbAccessController(IDatabaseAccessService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] decimal? userId, CancellationToken ct)
        => Ok(await _auth.GetDbAccessAsync(userId, ct));

    [RequireAdmin]
    [HttpPut("{userId}")]
    public async Task<IActionResult> ReplaceAsync(decimal userId, [FromBody] List<int> sageDbIds, CancellationToken ct)
    {
        await _auth.SetDbAccessAsync(userId, sageDbIds, ct);
        return NoContent();
    }
}
