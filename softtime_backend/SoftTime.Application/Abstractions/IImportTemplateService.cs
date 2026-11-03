using SoftTime.Application.DTOs;
using SoftTime.Application.ImportTemplates;

namespace SoftTime.Application.Abstractions;

/// <summary>Modèles d'import : description des colonnes attendues et fichier modèle téléchargeable.</summary>
public interface IImportTemplateService
{
    IReadOnlyList<ImportTemplateDto> List();

    /// <summary>Fichier modèle (.xlsx ou .csv) ; <see cref="NotFoundException"/> si la clé est inconnue.</summary>
    GeneratedFile Download(string key);
}
