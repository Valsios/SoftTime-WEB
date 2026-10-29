#nullable enable
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SoftTime.Domain.Services;

/// <summary>
/// Recherche textuelle partagée par les listes de l'application : normalisation du terme,
/// découpage en mots et correspondance insensible à la casse et aux accents.
/// </summary>
public static class SearchText
{
    public const int MaxLength = 100;

    /// <summary>Nettoie le terme saisi ; retourne <c>null</c> pour une recherche vide.</summary>
    public static string? Normalize(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var normalized = string.Join(' ', search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > MaxLength ? normalized[..MaxLength] : normalized;
    }

    /// <summary>Mots distincts du terme, normalisés pour la comparaison.</summary>
    public static IReadOnlyList<string> Terms(string? search)
    {
        var normalized = Normalize(search);
        if (normalized is null) return Array.Empty<string>();
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(ForComparison)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Mots distincts du terme sans transformation d'accent ou de casse.
    /// Utilisé pour les prédicats SQL, où la collation de la base gère la comparaison.
    /// </summary>
    public static IReadOnlyList<string> RawTerms(string? search)
    {
        var normalized = Normalize(search);
        if (normalized is null) return Array.Empty<string>();
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Vrai si tous les mots du terme sont présents dans au moins une des valeurs.
    /// Un terme vide ne filtre rien.
    /// </summary>
    public static bool Matches(string? search, params object?[] values)
    {
        var terms = Terms(search);
        if (terms.Count == 0) return true;
        var haystack = string.Join(
            ' ',
            values
                .Where(value => value is not null)
                .Select(ToText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(ForComparison));
        return terms.All(term => haystack.Contains(term, StringComparison.Ordinal));
    }

    /// <summary>Recherche par réflexion sur les propriétés publiques d'un DTO (hors identifiants).</summary>
    public static bool MatchesObject(string? search, object? row)
    {
        if (row is null) return false;
        var values = row
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && !IsTechnicalId(property.Name))
            .Select(property => property.GetValue(row))
            .ToArray();
        return Matches(search, values);
    }

    private static bool IsTechnicalId(string name) =>
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "OID", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "ID", StringComparison.OrdinalIgnoreCase);

    private static string ToText(object value) => value switch
    {
        string text => text,
        DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
        bool flag => flag ? "oui" : "non",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    private static string ForComparison(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var chars = decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }
}
