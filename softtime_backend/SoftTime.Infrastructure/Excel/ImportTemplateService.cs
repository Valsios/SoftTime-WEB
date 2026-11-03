using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.ImportTemplates;

namespace SoftTime.Infrastructure.Excel;

/// <summary>Catalogue des modèles d'import et génération du fichier modèle correspondant.</summary>
public sealed class ImportTemplateService : IImportTemplateService
{
    public IReadOnlyList<ImportTemplateDto> List()
        => ImportTemplateCatalog.All().Select(ImportTemplateCatalog.ToDto).ToList();

    public GeneratedFile Download(string key)
    {
        var template = ImportTemplateCatalog.Find(key)
            ?? throw new NotFoundException($"Modèle d'import inconnu : {key}.");
        return new GeneratedFile(ImportTemplateFile.Build(template), ImportTemplateFile.Mime(template), template.FileName);
    }
}
