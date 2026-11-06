using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/clock-params")]
[Tags("ClockParams")]
public class ClockParamsController : ControllerBase
{
    private readonly CatalogService _svc;
    public ClockParamsController(CatalogService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetClockAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ClockParamDto dto, CancellationToken ct)
        => Ok(await _svc.SaveClockAsync(dto, ct));
}
