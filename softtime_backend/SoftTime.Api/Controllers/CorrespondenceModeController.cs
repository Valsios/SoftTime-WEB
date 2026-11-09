using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/correspondence-mode")]
[Tags("CorrespondenceMode")]
public class CorrespondenceModeController : ApiControllerBase
{
    private readonly ICorrespondenceModeService _svc;
    public CorrespondenceModeController(ICorrespondenceModeService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _svc.GetCorrespondenceAsync(ct));

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] CorrespondenceModeDto dto, CancellationToken ct)
        => Ok(await _svc.SetCorrespondenceAsync(dto.Active, ct));
}
