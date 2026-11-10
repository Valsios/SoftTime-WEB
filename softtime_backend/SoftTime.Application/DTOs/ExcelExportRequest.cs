namespace SoftTime.Application.DTOs;

public record ExcelExportRequest(string FileName, string SheetName, List<string> Headers, List<List<string?>> Rows)
{
    public ExcelExportRequest() : this("export.xlsx", "Données", new List<string>(), new List<List<string?>>()) { }
}
