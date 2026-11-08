using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>Lecture et écriture de classeurs Excel (.xlsx).</summary>
public interface IExcelWorkbook
{
    /// <summary>Lignes de la première feuille, indexées par en-tête de colonne ; les lignes vides sont ignorées.</summary>
    List<Dictionary<string, string>> ReadFirstSheet(Stream stream);

    /// <summary>Classeur d'une feuille : en-têtes en ligne 1 puis valeurs converties en texte.</summary>
    byte[] WriteSheet(string sheetName, IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> data);

    /// <summary>Classeur multi-feuilles mis en forme (en-tête gras, figé et filtrable).</summary>
    byte[] WriteWorkbook(IEnumerable<ExcelSheetContent> sheets);
}
