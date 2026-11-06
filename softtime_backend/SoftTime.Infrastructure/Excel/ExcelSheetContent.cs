namespace SoftTime.Infrastructure.Excel;

/// <summary>Feuille prête à écrire : nom, en-têtes et lignes de valeurs typées.</summary>
public sealed record ExcelSheetContent(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);
