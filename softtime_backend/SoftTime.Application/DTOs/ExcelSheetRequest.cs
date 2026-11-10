namespace SoftTime.Application.DTOs;

/// <summary>Une feuille d'un classeur d'export : en-têtes + lignes typées (texte, nombre, booléen).</summary>
public record ExcelSheetRequest(string SheetName, List<string> Headers, List<List<object?>> Rows)
{
    public ExcelSheetRequest() : this("Données", new List<string>(), new List<List<object?>>()) { }
}
