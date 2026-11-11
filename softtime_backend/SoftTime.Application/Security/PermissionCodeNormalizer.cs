namespace SoftTime.Application.Security;

/// <summary>
/// Une seule règle pour enregistrer des permissions, d'un rôle comme d'un compte : codes nettoyés et dédoublonnés
/// (majuscules sauf le nom de base des codes SAGE:/POINTEUSE:), marqueurs internes retirés, codes trop longs, inconnus
/// ou mal formés refusés (400).
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
            .Where(code => code != PermissionCatalog.EmptyRoleMarker && code != PermissionCatalog.EmptyUserMarker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tooLong = normalized.Where(code => code.Length > MaxLength).ToList();
        if (tooLong.Count > 0)
            throw new DomainException($"Code de permission trop long ({MaxLength} caractères maximum) : {string.Join(", ", tooLong)}.");

        var unknown = normalized.Where(code => !PermissionCatalog.IsKnown(code)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Codes de permission inconnus : {string.Join(", ", unknown)}.");
        return normalized;
    }

    private static string Clean(string code)
    {
        var trimmed = code.Trim();
        return PermissionCatalog.IsDatabaseCode(trimmed) ? trimmed : trimmed.ToUpperInvariant();
    }
}
