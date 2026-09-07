using ClosedXML.Excel;

namespace SoftTime.Infrastructure.Excel;

public static class ExcelHelper
{
    public static List<Dictionary<string, string>> ReadFirstSheet(Stream stream)
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

    public static byte[] WriteSheet(string sheetName, IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> data)
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
