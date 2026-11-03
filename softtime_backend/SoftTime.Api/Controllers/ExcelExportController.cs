using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/excel")]
[Tags("Excel")]
public class ExcelExportController : ControllerBase
{
    private readonly IExcelExportService _excel;

    public ExcelExportController(IExcelExportService excel) => _excel = excel;

    [HttpPost("export")]
    public IActionResult Export([FromBody] ExcelExportRequest? request) => Download(_excel.Export(request));

    /// <summary>
    /// Classeur multi-feuilles : les données sont fournies par l'écran (déjà calculées et filtrées),
    /// aucune requête base n'est refaite — l'export reste conforme au contenu affiché.
    /// </summary>
    [HttpPost("export-workbook")]
    public IActionResult ExportWorkbook([FromBody] ExcelWorkbookRequest? request) => Download(_excel.ExportWorkbook(request));

    private FileContentResult Download(GeneratedFile file) => File(file.Content, file.ContentType, file.FileName);
}
