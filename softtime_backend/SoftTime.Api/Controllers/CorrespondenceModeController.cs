using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/correspondence-mode")]
[Tags("CorrespondenceMode")]
public class CorrespondenceModeController : ControllerBase
{
    private readonly CatalogService _svc;
    public CorrespondenceModeController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetCorrespondenceAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] CorrespondenceModeDto dto, CancellationToken ct)
        => Ok(await _svc.SetCorrespondenceAsync(dto.Active, ct));
}
