using System.Globalization;
using Microsoft.Data.SqlClient;

namespace SoftTime.Infrastructure.Readers;

internal static class SqlReaderHelper
{
    /// <summary>Lit une colonne quel que soit son type (texte, int, decimal...) et renvoie du texte nettoyé, ou null si vide.</summary>
    public static string? ReadText(SqlDataReader r, string column)
    {
        var v = r[column];
        if (v is null || v is DBNull) return null;
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}