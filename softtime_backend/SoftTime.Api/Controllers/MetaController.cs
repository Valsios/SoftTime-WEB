using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SoftTime.Api.Controllers;

[ApiController]
// Authentification exigée : la liste des routes et des numéros de droits est une carte de l'API pour un attaquant.
[Authorize]
[Route("api/meta")]
[Tags("Meta")]
public class MetaController : ControllerBase
{
    /// <summary>
    /// Catalogue des méthodes HTTP pour le frontend Angular (HttpClient).
    /// En-têtes: Authorization Bearer, X-Sage-Database, X-Pointeuse-Database.
    /// </summary>
    [HttpGet("endpoints")]
    public IActionResult Endpoints() => Ok(new
    {
        headers = new
        {
            Authorization = "Bearer {jwt}",
            XSageDatabase = "X-Sage-Database",
            XPointeuseDatabase = "X-Pointeuse-Database"
        },
        droits = new
        {
            databases = 1,
            parameters = 2,
            processing = 3,
            other = 4,
            traceability = 5,
            reports = 6
        },
        endpoints = ApiCatalog.All
    });
}
