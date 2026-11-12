using System.Text.RegularExpressions;

namespace SoftTime.Application.Security;

/// <summary>
/// Validation et normalisation des codes de permission avant enregistrement, commune aux rôles et aux utilisateurs.
/// <list type="bullet">
///   <item>codes vides ignorés, espaces retirés, doublons supprimés sans tenir compte de la casse ;</item>
///   <item>casse normalisée : majuscules, sauf le nom de base des codes SAGE: / POINTEUSE:, conservé tel quel ;</item>
///   <item>refus des codes de plus de 128 caractères (taille de la colonne) et des codes inconnus.</item>
/// </list>
/// Codes connus : ceux du <see cref="PermissionCatalog"/>, plus les codes de bases <c>SAGE:CREATE</c>,
/// <c>POINTEUSE:CREATE</c> et <c>SAGE|POINTEUSE:&lt;base&gt;:READ|UPDATE|DELETE|CREATE</c>
/// (création par base : lue par DatabaseConfigurationAuthorizer).
/// </summary>
public static class PermissionCodeValidator
{
    public const int MaxLength = 128;

    private static readonly Regex DatabaseCode = new(
        @"^(?<kind>SAGE|POINTEUSE):(?:(?<create>CREATE)|(?<base>.+):(?<action>READ|UPDATE|DELETE|CREATE))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Vrai si le code, une fois normalisé, peut être enregistré.</summary>
    public static bool IsValid(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var normalized = NormalizeCode(code);
        return normalized.Length <= MaxLength && IsKnownNormalized(normalized);
    }

    /// <summary>
    /// Ancien code de base sans action reconnue (ex. « SAGE:X ») : le calculateur des droits effectifs le compte
    /// (PermissionCatalog.IsKnown accepte tout préfixe SAGE: / POINTEUSE:, et il donne la porte Bases) mais la saisie le
    /// refuse et l'écran ne l'affiche pas. Les services l'ont déjà en base : ils le conservent tel quel à
    /// l'enregistrement, pour qu'une sauvegarde de l'écran ne retire aucun accès existant.
    /// </summary>
    public static bool IsLegacyDatabaseCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && PermissionCatalog.IsKnown(code) && !IsValid(code);

    /// <summary>Codes enregistrés : saisie validée, plus les anciens codes de base déjà présents (voir IsLegacyDatabaseCode).</summary>
    public static List<string> WithLegacyDatabaseCodes(List<string> validated, IEnumerable<string> stored)
        => validated
            .Concat(stored.Where(IsLegacyDatabaseCode).Where(c => !validated.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Codes prêts à enregistrer (marqueur <paramref name="emptyMarker"/> retiré). Lève une <see cref="DomainException"/>
    /// avec la liste des codes refusés : trop longs d'abord, puis inconnus.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string>? permissions, string emptyMarker)
    {
        if (permissions is null)
            throw new DomainException("La liste des permissions ne peut pas être nulle.");

        var normalized = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.Trim() != emptyMarker)
            .Select(NormalizeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tooLong = normalized.Where(p => p.Length > MaxLength).ToList();
        if (tooLong.Count > 0)
            throw new DomainException($"Code de permission trop long ({MaxLength} caractères maximum) : {string.Join(", ", tooLong)}.");

        var unknown = normalized.Where(p => !IsKnownNormalized(p)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Codes de permission inconnus : {string.Join(", ", unknown)}.");
        return normalized;
    }

    private static string NormalizeCode(string code)
    {
        var trimmed = code.Trim();
        var match = DatabaseCode.Match(trimmed);
        if (!match.Success)
            return trimmed.ToUpperInvariant();
        var kind = match.Groups["kind"].Value.ToUpperInvariant();
        return match.Groups["create"].Success
            ? $"{kind}:CREATE"
            : $"{kind}:{match.Groups["base"].Value}:{match.Groups["action"].Value.ToUpperInvariant()}";
    }

    private static bool IsKnownNormalized(string code)
        => DatabaseCode.IsMatch(code) || PermissionCatalog.KnownStatic.Contains(code);
}
