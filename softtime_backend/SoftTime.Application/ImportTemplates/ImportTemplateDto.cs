#nullable enable

namespace SoftTime.Application.ImportTemplates;

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
