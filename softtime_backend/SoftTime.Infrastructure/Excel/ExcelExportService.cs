using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Excel;

/// <summary>
/// Classeurs Excel construits à partir des données fournies par l'écran (déjà calculées et filtrées) :
/// l'export reste conforme au contenu affiché.
/// </summary>
public sealed class ExcelExportService : IExcelExportService
{
    private const string DefaultSheetName = "Données";

    public GeneratedFile Export(ExcelExportRequest? request)
    {
        request ??= new ExcelExportRequest();
        var sheet = string.IsNullOrWhiteSpace(request.SheetName) ? DefaultSheetName : request.SheetName;
        var rows = (request.Rows ?? new List<List<string?>>()).Select(r => (r ?? new List<string?>()).Cast<object?>());
        var bytes = ExcelHelper.WriteSheet(sheet, request.Headers ?? new List<string>(), rows);
        return new GeneratedFile(bytes, GeneratedFile.ExcelContentType, NormalizeFileName(request.FileName));
    }

    public GeneratedFile ExportWorkbook(ExcelWorkbookRequest? request)
    {
        request ??= new ExcelWorkbookRequest();
        var sheets = (request.Sheets ?? new List<ExcelSheetRequest>()).Select(ToSheet).ToList();
        return new GeneratedFile(ExcelHelper.WriteWorkbook(sheets), GeneratedFile.ExcelContentType, NormalizeFileName(request.FileName));
    }

    private static ExcelSheetContent ToSheet(ExcelSheetRequest sheet) => new(
        string.IsNullOrWhiteSpace(sheet.SheetName) ? DefaultSheetName : sheet.SheetName,
        sheet.Headers ?? new List<string>(),
        (sheet.Rows ?? new List<List<object?>>()).Select(r => (IReadOnlyList<object?>)(r ?? new List<object?>())).ToList());

    private static string NormalizeFileName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "export.xlsx" : fileName;
        return name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? name : name + ".xlsx";
    }
}
