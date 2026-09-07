using System.Globalization;
using System.Text;

namespace SoftTime.Application.Services;

public static class ExcelCells
{
    public static string? Get(IReadOnlyDictionary<string, string> row, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var foldedAlias = Fold(alias);
            foreach (var kv in row)
            {
                if (Fold(kv.Key) == foldedAlias)
                    return kv.Value;
            }
        }
        return null;
    }

    public static bool TryTime(string? raw, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out time))
            return true;
        if (TimeSpan.TryParse(s, CultureInfo.GetCultureInfo("fr-FR"), out time))
            return true;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            || DateTime.TryParse(s, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out dt))
        {
            time = dt.TimeOfDay;
            return true;
        }
        return false;
    }

    public static bool TryDate(string? raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        var cultures = new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("fr-FR"), CultureInfo.GetCultureInfo("en-US") };
        foreach (var culture in cultures)
        {
            if (DateTime.TryParse(s, culture, DateTimeStyles.None, out date))
            {
                date = date.Date;
                return true;
            }
        }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa)
            && oa > 20000 && oa < 80000)
        {
            date = DateTime.FromOADate(oa).Date;
            return true;
        }
        return false;
    }

    public static string Fold(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    }
}
