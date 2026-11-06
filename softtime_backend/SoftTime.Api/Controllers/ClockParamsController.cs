using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/clock-params")]
[Tags("ClockParams")]
public class ClockParamsController : ControllerBase
{
    private readonly IClockParameterService _svc;
    public ClockParamsController(IClockParameterService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetClockAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ClockParamDto dto, CancellationToken ct)
        => Ok(await _svc.SaveClockAsync(dto, ct));
}
