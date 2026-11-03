using SoftTime.Application.Abstractions;

namespace SoftTime.Infrastructure.Excel;

/// <summary>Lecture des fichiers d'import : Excel via ClosedXML, CSV point-virgule (ou virgule).</summary>
public sealed class ImportFileReader : IImportFileReader
{
    private static readonly char[] LineBreaks = { '\r', '\n' };

    public List<Dictionary<string, string>> ReadExcelRows(Stream stream) => ExcelHelper.ReadFirstSheet(stream);

    public async Task<IReadOnlyList<string[]>> ReadCsvLinesAsync(Stream stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(ct);
        return text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries)
            .Select(SplitLine)
            .ToList();
    }

    private static string[] SplitLine(string line)
    {
        var separator = line.Contains(';') ? ';' : ',';
        return line.Split(separator).Select(cell => cell.Trim().Trim('"')).ToArray();
    }
}
