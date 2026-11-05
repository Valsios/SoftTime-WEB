using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Script versionné de migrations/versions (NNN_description.sql).</summary>
public sealed record MigrationScript(int Version, string Name, string Content)
{
    /// <summary>
    /// SHA-256 hexadécimal du script. Les fins de ligne sont normalisées en LF avant le calcul :
    /// un même fichier extrait en CRLF (Windows) ou en LF donne la même somme de contrôle.
    /// </summary>
    public string Checksum { get; } = ComputeChecksum(Content);

    public static string ComputeChecksum(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}

public interface IMigrationScriptSource
{
    /// <summary>Scripts disponibles, triés par numéro de version croissant.</summary>
    IReadOnlyList<MigrationScript> Load();
}

/// <summary>Nommage et tri des scripts : NNN_description.sql, ordre numérique, numéros uniques.</summary>
public static class MigrationScriptNames
{
    private static readonly Regex Pattern = new(@"^(\d{3})_[A-Za-z0-9_]+\.sql$", RegexOptions.CultureInvariant);

    public static bool TryParseVersion(string fileName, out int version)
    {
        version = 0;
        var match = Pattern.Match(fileName);
        return match.Success && int.TryParse(match.Groups[1].Value, out version) && version > 0;
    }

    public static IReadOnlyList<MigrationScript> Order(IEnumerable<MigrationScript> scripts)
    {
        var ordered = scripts.OrderBy(s => s.Version).ToList();
        var duplicate = ordered.GroupBy(s => s.Version).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException(
                $"Deux scripts de migration portent le numéro {duplicate.Key:D3} : {string.Join(", ", duplicate.Select(s => s.Name))}.");
        return ordered;
    }
}

/// <summary>Scripts embarqués comme ressources de SoftTime.Infrastructure (voir le .csproj).</summary>
public sealed class EmbeddedMigrationScriptSource : IMigrationScriptSource
{
    public const string ResourcePrefix = "SoftTime.Migrations.Versions.";
    private readonly Assembly _assembly;

    public EmbeddedMigrationScriptSource(Assembly? assembly = null)
        => _assembly = assembly ?? typeof(EmbeddedMigrationScriptSource).Assembly;

    public IReadOnlyList<MigrationScript> Load()
    {
        var scripts = new List<MigrationScript>();
        foreach (var resource in _assembly.GetManifestResourceNames().Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var fileName = resource[ResourcePrefix.Length..];
            if (!MigrationScriptNames.TryParseVersion(fileName, out var version))
                throw new InvalidOperationException(
                    $"Le script de migration « {fileName} » ne respecte pas le nommage NNN_description.sql.");
            using var stream = _assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            scripts.Add(new MigrationScript(version, fileName, reader.ReadToEnd()));
        }
        return MigrationScriptNames.Order(scripts);
    }
}

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
