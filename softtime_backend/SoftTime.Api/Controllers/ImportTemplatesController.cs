using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.ImportTemplates;
using SoftTime.Infrastructure.Excel;

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
    /// <summary>Liste des modèles : libellé, format, colonnes, exemple et règles.</summary>
    [HttpGet]
    public IActionResult List()
        => Ok(ImportTemplateCatalog.All().Select(ImportTemplateCatalog.ToDto).ToList());

    /// <summary>Télécharge le modèle (.xlsx ou .csv) d'une clé du catalogue.</summary>
    [HttpGet("{key}")]
    public IActionResult Download(string key)
    {
        var template = ImportTemplateCatalog.Find(key);
        if (template is null)
            return NotFound($"Modèle d'import inconnu : {key}.");

        return File(ImportTemplateFile.Build(template), ImportTemplateFile.Mime(template), template.FileName);
    }
}
