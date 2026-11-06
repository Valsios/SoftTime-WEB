namespace SoftTime.Application.DTOs;

/// <summary>Classeur multi-feuilles généré côté serveur (données déjà calculées par l'écran).</summary>
public record ExcelWorkbookRequest(string FileName, List<ExcelSheetRequest> Sheets)
{
    public ExcelWorkbookRequest() : this("export.xlsx", new List<ExcelSheetRequest>()) { }
}
