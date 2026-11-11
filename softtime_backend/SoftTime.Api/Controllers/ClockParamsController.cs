using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clock-params")]
[Tags("ClockParams")]
public class ClockParamsController : ApiControllerBase
{
    private readonly IClockParameterService _svc;
    public ClockParamsController(IClockParameterService svc) => _svc = svc;

    /// <summary>Mode de pointage (référentiel sans donnée de salarié), affiché aussi par la validation hebdomadaire (porte 3).</summary>
    [RequireAnyDroit(Droit.Parameters, Droit.Processing)]
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _svc.GetClockAsync(ct));

    [RequireDroit(Droit.Parameters)]
    [RequirePermission(Droit.Parameters, "PARAMS:CLOCK_PARAMS")]
    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] ClockParamDto dto, CancellationToken ct)
        => Ok(await _svc.SaveClockAsync(dto, ct));
}
