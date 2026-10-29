#nullable enable

namespace SoftTime.Application.ImportTemplates;

/// <summary>Colonne attendue par un import, telle qu'exposée dans l'aide « Modèles d'import ».</summary>
public sealed record ImportTemplateColumn(
    string Header,
    bool Required,
    string Format,
    string Example,
    string? Note = null);

/// <summary>
/// Définition d'un modèle d'import : matrice des colonnes, exemple de fichier et règles.
/// Les en-têtes sont exactement ceux lus par les parseurs (<c>ExcelCells</c> pour les imports Excel,
/// première ligne pour les CSV) : le modèle téléchargé est donc directement importable.
/// </summary>
public sealed record ImportTemplateDefinition(
    string Key,
    string Label,
    string Format,
    string FileName,
    string SheetName,
    string Description,
    IReadOnlyList<ImportTemplateColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> ExampleRows,
    IReadOnlyList<string> Notes)
{
    public IReadOnlyList<string> Headers { get; } = Columns.Select(c => c.Header).ToList();
}

/// <summary>Colonne d'un modèle d'import, contrat de l'API d'aide.</summary>
public sealed record ImportTemplateColumnDto(
    string Header,
    bool Required,
    string Format,
    string Example,
    string? Note);

/// <summary>Modèle d'import exposé à l'écran : matrice, exemple et règles.</summary>
public sealed record ImportTemplateDto(
    string Key,
    string Label,
    string Format,
    string FileName,
    string Description,
    IReadOnlyList<ImportTemplateColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<string>> ExampleRows,
    IReadOnlyList<string> Notes);
