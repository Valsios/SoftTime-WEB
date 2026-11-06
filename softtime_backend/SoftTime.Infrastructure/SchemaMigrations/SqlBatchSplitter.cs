using System.Text;
using System.Text.RegularExpressions;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>
/// Découpe un script sur les lignes GO (séparateur de lots de sqlcmd / SSMS), insensible à la
/// casse, avec un éventuel nombre de répétitions (GO 3) et un commentaire de fin de ligne.
/// Une ligne GO à l'intérieur d'un commentaire /* ... */ n'est pas un séparateur.
/// </summary>
public static class SqlBatchSplitter
{
    private static readonly Regex GoLine = new(@"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Split(string script)
    {
        var batches = new List<string>();
        var current = new StringBuilder();
        var commentDepth = 0;
        foreach (var line in script.Replace("\r\n", "\n").Split('\n'))
        {
            var match = commentDepth == 0 ? GoLine.Match(line) : Match.Empty;
            if (match.Success)
            {
                var repeat = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1;
                AddBatch(batches, current.ToString(), repeat);
                current.Clear();
                continue;
            }
            current.Append(line).Append('\n');
            commentDepth = CommentDepthAfter(line, commentDepth);
        }
        AddBatch(batches, current.ToString(), 1);
        return batches;
    }

    private static void AddBatch(List<string> batches, string batch, int repeat)
    {
        if (string.IsNullOrWhiteSpace(batch))
            return;
        for (var i = 0; i < repeat; i++)
            batches.Add(batch.TrimEnd());
    }

    // Profondeur des commentaires de bloc en fin de ligne ; un « -- » hors bloc clôt l'analyse.
    private static int CommentDepthAfter(string line, int depth)
    {
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (depth == 0 && line[i] == '-' && line[i + 1] == '-')
                break;
            if (line[i] == '/' && line[i + 1] == '*') { depth++; i++; }
            else if (depth > 0 && line[i] == '*' && line[i + 1] == '/') { depth--; i++; }
        }
        return depth;
    }
}
