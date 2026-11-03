using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>Export Excel des données déjà affichées par l'écran (aucune requête base n'est refaite).</summary>
public interface IExcelExportService
{
    GeneratedFile Export(ExcelExportRequest? request);

    GeneratedFile ExportWorkbook(ExcelWorkbookRequest? request);
}
