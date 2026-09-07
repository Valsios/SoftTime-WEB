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
        var name = string.IsNullOrWhiteSpace(request.FileName) ? "export.xlsx" : request.FileName;
        if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            name += ".xlsx";
        var sheet = string.IsNullOrWhiteSpace(request.SheetName) ? "Données" : request.SheetName;
        var headers = request.Headers ?? new List<string>();
        var rows = request.Rows ?? new List<List<string?>>();
        var bytes = ExcelHelper.WriteSheet(sheet, headers, rows.Select(r => (r ?? new List<string?>()).Cast<object?>()));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }
}
