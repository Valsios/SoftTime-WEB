namespace SoftTime.Application.Security;

/// <summary>
/// Une seule règle pour enregistrer des permissions, d'un rôle comme d'un compte : codes nettoyés et dédoublonnés
/// (en minuscules), marqueurs internes retirés ; codes trop longs, inconnus, mal formés, ou action d'écran sans la page
/// de son écran, refusés (400).
/// </summary>
public static class PermissionCodeNormalizer
{
    public const int MaxLength = 128;

    public static List<string> Normalize(IEnumerable<string?>? permissions)
    {
        if (permissions is null)
            throw new DomainException("La liste des permissions ne peut pas être nulle.");

        var normalized = permissions
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => Clean(code!))
            .Where(code => !IsMarker(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tooLong = normalized.Where(code => code.Length > MaxLength).ToList();
        if (tooLong.Count > 0)
            throw new DomainException($"Code de permission trop long ({MaxLength} caractères maximum) : {string.Join(", ", tooLong)}.");

        var unknown = normalized.Where(code => !PermissionCatalog.IsKnown(code)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Codes de permission inconnus : {string.Join(", ", unknown)}.");

        var orphans = normalized.Where(code => PermissionCatalog.PageOf(code) is string page && !normalized.Contains(page, StringComparer.OrdinalIgnoreCase))
            .Select(code => $"{code} (exige {PermissionCatalog.PageOf(code)})")
            .ToList();
        if (orphans.Count > 0)
            throw new DomainException($"Une action exige la page de son écran : {string.Join(", ", orphans)}.");
        return normalized;
    }

    private static string Clean(string code) => code.Trim().ToLowerInvariant();

    private static bool IsMarker(string code)
        => string.Equals(code, PermissionCatalog.EmptyRoleMarker, StringComparison.OrdinalIgnoreCase)
           || string.Equals(code, PermissionCatalog.EmptyUserMarker, StringComparison.OrdinalIgnoreCase);
}
