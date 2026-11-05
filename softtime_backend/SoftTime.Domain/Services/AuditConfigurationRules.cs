using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftTime.Domain.Services;

public record AuditTypeOption(string Value, string Label);

public static class AuditConfigurationRules
{
    private static readonly HashSet<string> ExcludedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text",
        "ntext",
        "image"
    };

    private static readonly List<AuditTypeOption> Types = new()
    {
        // Types texte
        new("varchar", "Texte variable (varchar)"),
        new("nvarchar", "Texte Unicode variable (nvarchar)"),
        new("char", "Caractères fixes (char)"),
        new("nchar", "Caractères fixes Unicode (nchar)"),

        // Types entiers
        new("int", "Entier (int)"),
        new("smallint", "Petit entier (smallint)"),
        new("tinyint", "Très petit entier (tinyint)"),
        new("bigint", "Grand entier (bigint)"),

        // Types décimaux et réels
        new("decimal", "Décimal (decimal)"),
        new("numeric", "Numérique précis (numeric)"),
        new("float", "Nombre flottant (float)"),
        new("real", "Réel (real)"),
        new("money", "Monétaire (money)"),
        new("smallmoney", "Petit monétaire (smallmoney)"),

        // Types date et heure
        new("datetime", "Date et heure (datetime)"),
        new("datetime2", "Date et heure étendue (datetime2)"),
        new("smalldatetime", "Date et heure courte (smalldatetime)"),
        new("date", "Date (date)"),
        new("time", "Heure (time)"),

        // Type logique
        new("bit", "Booléen (bit)"),

        // Identifiant
        new("uniqueidentifier", "Identifiant unique (uniqueidentifier)")
    };

    private static readonly HashSet<string> AllowedSet = new(
        Types.Select(t => t.Value),
        StringComparer.OrdinalIgnoreCase
    );

    public static IReadOnlyList<AuditTypeOption> AllowedTypeOptions => Types;

    public static bool IsExcluded(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        return ExcludedTypes.Contains(type.Trim());
    }

    public static bool IsValid(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        var trimmed = type.Trim();
        return !ExcludedTypes.Contains(trimmed) && AllowedSet.Contains(trimmed);
    }

    public static string Normalize(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException("Le type de données est obligatoire.");
        var trimmed = type.Trim();
        if (!IsValid(trimmed))
            throw new InvalidOperationException($"Le type de données '{type}' n'est pas autorisé.");
        return trimmed.ToLowerInvariant();
    }

    public static void Valider(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException("Le type de données est obligatoire.");

        var trimmed = type.Trim();
        if (ExcludedTypes.Contains(trimmed))
            throw new InvalidOperationException($"Le type de données '{type}' est obsolète ou incompatible avec l'audit (exclu : text, ntext, image).");

        if (!AllowedSet.Contains(trimmed))
            throw new InvalidOperationException($"Le type de données '{type}' n'est pas autorisé.");
    }
}
