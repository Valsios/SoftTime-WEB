#nullable enable

namespace SoftTime.Application.ImportTemplates;

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
