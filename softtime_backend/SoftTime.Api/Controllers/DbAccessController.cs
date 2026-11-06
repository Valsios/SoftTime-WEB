using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/db-access")]
[Tags("DbAccess")]
public class DbAccessController : ControllerBase
{
    private readonly IAuthService _auth;
    public DbAccessController(IAuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] decimal? userId, CancellationToken ct)
        => Ok(await _auth.GetDbAccessAsync(userId, ct));

    [RequireAdmin]
    [HttpPut("{userId}")]
    public async Task<IActionResult> Replace(decimal userId, [FromBody] List<int> sageDbIds, CancellationToken ct)
    {
        await _auth.SetDbAccessAsync(userId, sageDbIds, ct);
        return NoContent();
    }
}
