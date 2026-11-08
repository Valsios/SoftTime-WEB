using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[RequireDroit(Droit.Parameters)]
[Route("api/v1/clock-params")]
[Tags("ClockParams")]
public class ClockParamsController : ApiControllerBase
{
    private readonly IClockParameterService _svc;
    public ClockParamsController(IClockParameterService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _svc.GetClockAsync(ct));

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] ClockParamDto dto, CancellationToken ct)
        => Ok(await _svc.SaveClockAsync(dto, ct));
}
