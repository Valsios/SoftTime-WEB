using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Aide à l'import : matrice des colonnes attendues et fichier modèle téléchargeable pour chaque
/// import (Excel et CSV). Le contenu vient du catalogue applicatif, aligné sur les parseurs :
/// un modèle téléchargé est directement importable par l'écran concerné.
/// </summary>
[ApiController]
[Authorize]
[Route("api/import-templates")]
[Tags("ImportTemplates")]
public class ImportTemplatesController : ControllerBase
{
    private readonly IImportTemplateService _templates;

    public ImportTemplatesController(IImportTemplateService templates) => _templates = templates;

    /// <summary>Liste des modèles : libellé, format, colonnes, exemple et règles.</summary>
    [HttpGet]
    public IActionResult List() => Ok(_templates.List());

    /// <summary>Télécharge le modèle (.xlsx ou .csv) d'une clé du catalogue (404 si la clé est inconnue).</summary>
    [HttpGet("{key}")]
    public IActionResult Download(string key)
    {
        var file = _templates.Download(key);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
