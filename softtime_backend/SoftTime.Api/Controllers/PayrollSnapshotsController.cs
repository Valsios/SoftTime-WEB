using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Authorization;
using SoftTime.Application.Abstractions.Payroll;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Instantanés des envois en paie de la base active, en LECTURE SEULE : aucune route d'écriture ni de suppression
/// (POST, PUT, PATCH, DELETE répondent 405). Mêmes droits que la lecture des variables envoyées : porte Bases et
/// permission SAGE:{base}:UPDATE (ou administrateur), vérifiées par le service.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/payroll-snapshots")]
[Tags("PayrollSnapshots")]
public class PayrollSnapshotsController : ApiControllerBase
{
    private readonly IPayrollSnapshotService _svc;

    public PayrollSnapshotsController(IPayrollSnapshotService svc) => _svc = svc;

    /// <summary>Envois de la période exacte (du plus récent au plus ancien), avec empreinte et filtre de chaque instantané.</summary>
    [HttpGet]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> ListAsync([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Ok(await _svc.ListAsync(from, to, ct));

    /// <summary>Instantané d'un envoi : entête et lignes figées.</summary>
    [HttpGet("{envoiId:int}")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> GetAsync(int envoiId, CancellationToken ct) => Ok(await _svc.GetAsync(envoiId, ct));

    /// <summary>Vérification : empreinte recalculée depuis les lignes relues, comparée à l'empreinte enregistrée.</summary>
    [HttpGet("{envoiId:int}/verification")]
    [RequireDroit(Droit.Databases)]
    public async Task<IActionResult> VerifyAsync(int envoiId, CancellationToken ct) => Ok(await _svc.VerifyAsync(envoiId, ct));
}
