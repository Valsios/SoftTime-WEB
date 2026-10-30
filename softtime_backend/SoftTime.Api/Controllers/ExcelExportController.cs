using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.DTOs;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/excel")]
[Tags("Excel")]
public class ExcelExportController : ControllerBase
{
    [HttpPost("export")]
    public IActionResult Export([FromBody] ExcelExportRequest? request)
    {
        request ??= new ExcelExportRequest();
        var name = NormalizeFileName(request.FileName);
        var sheet = string.IsNullOrWhiteSpace(request.SheetName) ? "Données" : request.SheetName;
        var headers = request.Headers ?? new List<string>();
        var rows = request.Rows ?? new List<List<string?>>();
        var bytes = ExcelHelper.WriteSheet(sheet, headers, rows.Select(r => (r ?? new List<string?>()).Cast<object?>()));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>
    /// Classeur multi-feuilles : les données sont fournies par l'écran (déjà calculées et filtrées),
    /// aucune requête base n'est refaite — l'export reste conforme au contenu affiché.
    /// </summary>
    [HttpPost("export-workbook")]
    public IActionResult ExportWorkbook([FromBody] ExcelWorkbookRequest? request)
    {
        request ??= new ExcelWorkbookRequest();
        var name = NormalizeFileName(request.FileName);
        var sheets = (request.Sheets ?? new List<ExcelSheetRequest>())
            .Select(s => new ExcelSheetContent(
                string.IsNullOrWhiteSpace(s.SheetName) ? "Données" : s.SheetName,
                s.Headers ?? new List<string>(),
                (s.Rows ?? new List<List<object?>>())
                    .Select(r => (IReadOnlyList<object?>)(r ?? new List<object?>()))
                    .ToList()))
            .ToList();
        var bytes = ExcelHelper.WriteWorkbook(sheets);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    private static string NormalizeFileName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "export.xlsx" : fileName;
        return name!.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? name : name + ".xlsx";
    }
}
