#nullable enable

using System.Text;
using SoftTime.Application.ImportTemplates;

namespace SoftTime.Infrastructure.Excel;

/// <summary>
/// Génère le fichier modèle d'un <see cref="ImportTemplateDefinition"/> : `.xlsx` via
/// <see cref="ExcelHelper.WriteSheet"/> (ClosedXML) ou `.csv` UTF-8 (BOM, séparateur « ; ») prêt
/// à être ouvert dans Excel/LibreOffice. Le contenu (en-têtes + lignes d'exemple) est celui du
/// catalogue : le fichier est donc directement importable par l'endpoint correspondant.
/// </summary>
public static class ImportTemplateFile
{
    public static byte[] Build(ImportTemplateDefinition template)
        => string.Equals(template.Format, ImportTemplateCatalog.FormatCsv, StringComparison.OrdinalIgnoreCase)
            ? ToCsv(template)
            : ToXlsx(template);

    public static byte[] ToXlsx(ImportTemplateDefinition template)
        => ExcelHelper.WriteSheet(template.SheetName, template.Headers, template.ExampleRows);

    public static byte[] ToCsv(ImportTemplateDefinition template)
    {
        var sb = new StringBuilder();
        // BOM : Excel reconnaît ainsi l'UTF-8 et affiche correctement les accents.
        sb.Append('\uFEFF');
        sb.Append(Ligne(template.Headers));
        sb.Append("\r\n");
        foreach (var row in template.ExampleRows)
        {
            sb.Append(Ligne(row));
            sb.Append("\r\n");
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    public static string Mime(ImportTemplateDefinition template)
        => string.Equals(template.Format, ImportTemplateCatalog.FormatCsv, StringComparison.OrdinalIgnoreCase)
            ? "text/csv; charset=utf-8"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static string Ligne(IReadOnlyList<string> cells)
        => string.Join(";", cells.Select(Echapper));

    private static string Echapper(string? value)
    {
        var texte = value ?? string.Empty;
        return texte.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0
            ? "\"" + texte.Replace("\"", "\"\"") + "\""
            : texte;
    }
}
