using System.Globalization;
using System.Text.Json;

using ClosedXML.Excel;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Excel;

/// <summary>Classeurs Excel via ClosedXML.</summary>
public sealed class ExcelWorkbook : IExcelWorkbook
{
    public List<Dictionary<string, string>> ReadFirstSheet(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var rows = new List<Dictionary<string, string>>();
        var range = ws.RangeUsed();
        if (range == null)
            return rows;

        var firstRow = range.FirstRow().RowNumber();
        var lastRow = range.LastRow().RowNumber();
        var firstCol = range.FirstColumn().ColumnNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var c = firstCol; c <= lastCol; c++)
        {
            var text = CellText(ws.Cell(firstRow, c));
            headers.Add(string.IsNullOrWhiteSpace(text) ? $"Col{c}" : text);
        }

        for (var r = firstRow + 1; r <= lastRow; r++)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
                dict[headers[i]] = CellText(ws.Cell(r, firstCol + i));
            if (dict.Values.All(string.IsNullOrWhiteSpace))
                continue;
            rows.Add(dict);
        }

        return rows;
    }

    public byte[] WriteSheet(string sheetName, IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> data)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(sheetName);
        var h = headers.ToList();
        for (var i = 0; i < h.Count; i++)
            ws.Cell(1, i + 1).Value = h[i];
        var r = 2;
        foreach (var row in data)
        {
            var c = 1;
            foreach (var val in row)
            {
                ws.Cell(r, c).Value = val?.ToString() ?? string.Empty;
                c++;
            }
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Écrit un classeur multi-feuilles : en-têtes en gras, ligne d'en-tête figée, filtre
    /// automatique et largeurs ajustées. Au moins une feuille est toujours produite.
    /// </summary>
    public byte[] WriteWorkbook(IEnumerable<ExcelSheetContent> sheets)
    {
        using var wb = new XLWorkbook();
        var feuilles = (sheets ?? Enumerable.Empty<ExcelSheetContent>()).ToList();
        if (feuilles.Count == 0)
            feuilles.Add(new ExcelSheetContent("Données", Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>()));

        var nomsUtilises = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feuille in feuilles)
        {
            var ws = wb.AddWorksheet(NomFeuilleUnique(feuille.Name, nomsUtilises));
            var headers = feuille.Headers ?? Array.Empty<string>();
            for (var i = 0; i < headers.Count; i++)
                ws.Cell(1, i + 1).Value = headers[i] ?? string.Empty;
            EcrireLignes(ws, feuille.Rows ?? Array.Empty<IReadOnlyList<object?>>());
            MettreEnForme(ws, headers.Count > 0);
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Données à partir de la ligne 2 ; une ligne nulle laisse une ligne vide.</summary>
    private static void EcrireLignes(IXLWorksheet ws, IEnumerable<IReadOnlyList<object?>> lignes)
    {
        var r = 2;
        foreach (var ligne in lignes)
        {
            if (ligne is not null)
            {
                var c = 1;
                foreach (var valeur in ligne)
                    SetCellValue(ws.Cell(r, c++), valeur);
            }
            r++;
        }
    }

    /// <summary>En-tête en gras, figé et filtrable ; colonnes ajustées au contenu, 45 de large au plus.</summary>
    private static void MettreEnForme(IXLWorksheet ws, bool avecEntete)
    {
        if (avecEntete)
        {
            ws.Row(1).Style.Font.Bold = true;
            ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F7F9");
            ws.SheetView.FreezeRows(1);
            ws.RangeUsed()?.SetAutoFilter();
        }

        ws.ColumnsUsed().AdjustToContents();
        foreach (var colonne in ws.ColumnsUsed().Where(c => c.Width > 45))
            colonne.Width = 45;
    }

    /// <summary>Nom de feuille Excel valide (31 caractères, caractères interdits remplacés) et unique.</summary>
    private static string NomFeuilleUnique(string? nom, HashSet<string> utilises)
    {
        var brut = string.IsNullOrWhiteSpace(nom) ? "Données" : nom!;
        var propre = new string(brut
            .Select(c => c is '[' or ']' or ':' or '*' or '?' or '/' or '\\' ? '-' : c)
            .ToArray())
            .Trim()
            .Trim('\'');
        if (propre.Length == 0)
            propre = "Données";
        if (propre.Length > 31)
            propre = propre[..31];

        var candidat = propre;
        var suffixe = 2;
        while (!utilises.Add(candidat))
        {
            var marque = $" ({suffixe++})";
            candidat = propre.Length + marque.Length > 31
                ? propre[..(31 - marque.Length)] + marque
                : propre + marque;
        }
        return candidat;
    }

    /// <summary>
    /// Écrit une valeur typée : les nombres restent numériques, les booléens booléens, les
    /// chaînes restent du texte. Les valeurs JSON désérialisées en <see cref="JsonElement"/>
    /// sont ramenées au type primitif correspondant.
    /// </summary>
    private static void SetCellValue(IXLCell cell, object? valeur)
    {
        if (valeur is null)
            return;

        if (valeur is JsonElement json)
            valeur = FromJson(json);
        if (valeur is null)
            return;
        if (valeur is string texte && texte.Length == 0)
            return;

        try
        {
            cell.Value = XLCellValue.FromObject(valeur, CultureInfo.InvariantCulture);
        }
        catch (InvalidCastException)
        {
            cell.Value = valeur.ToString() ?? string.Empty;
        }
    }

    private static object? FromJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.String => json.GetString(),
        JsonValueKind.Number => json.TryGetInt64(out var entier) ? entier : json.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => json.ToString(),
    };

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return string.Empty;
        if (cell.TryGetValue(out DateTime dt))
        {
            if (dt.Year < 1900)
                return dt.ToString("HH:mm:ss");
            return dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("yyyy-MM-dd")
                : dt.ToString("yyyy-MM-dd HH:mm:ss");
        }
        return cell.GetFormattedString().Trim();
    }
}
