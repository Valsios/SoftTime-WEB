namespace SoftTime.Api.Controllers;

/// <summary>
/// Extrait les noms de bases autorisées à partir des permissions détaillées
/// (<c>SAGE:{nom}:READ</c> / <c>POINTEUSE:{nom}:READ</c>).
/// </summary>
internal static class DatabasePermissionNames
{
    public static IReadOnlyCollection<string> Read(IEnumerable<string> permissions, string system)
    {
        var prefix = system + ":";
        return permissions
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && p.EndsWith(":READ", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Split(':'))
            .Where(parts => parts.Length == 3)
            .Select(parts => parts[1])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
