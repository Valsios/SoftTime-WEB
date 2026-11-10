using System.Text.RegularExpressions;

namespace SoftTime.Infrastructure.SchemaMigrations;

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
