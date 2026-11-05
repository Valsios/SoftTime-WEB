using System.Text.RegularExpressions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>Conventions partagées par la configuration et le writer de paie AUTRE.</summary>
public static class PayrollWritingConfiguration
{
    public static readonly string[] Categories = ["Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit"];
    private static readonly Regex IdentifierPart = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public static string NormalizeCategory(string? category) => Categories.FirstOrDefault(c =>
        string.Equals(c, category?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Catégorie de paie inconnue : '{category}'.");

    public static void ValidateIdentifier(string? value, string label, bool allowQualifiedTable = false)
    {
        var parts = (value ?? string.Empty).Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || (!allowQualifiedTable && parts.Length != 1) || (allowQualifiedTable && parts.Length > 2)
            || parts.Any(part => !IdentifierPart.IsMatch(part)))
            throw new InvalidOperationException($"{label} contient un identifiant SQL invalide.");
    }

    public static string QuoteIdentifier(string value, bool qualifiedTable = false)
    {
        ValidateIdentifier(value, "L'identifiant SQL", qualifiedTable);
        return string.Join('.', value.Split('.', StringSplitOptions.TrimEntries).Select(part => $"[{part}]"));
    }

    public static (string Schema, string Table) SplitTable(string table)
    {
        ValidateIdentifier(table, "La table cible", allowQualifiedTable: true);
        var parts = table.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length == 1 ? ("dbo", parts[0]) : (parts[0], parts[1]);
    }

    public static void ValidateConfigurationRows(IReadOnlyList<T_PAIE_ECRITURE> configs)
    {
        var missing = Categories.Where(c => !configs.Any(x => string.Equals(x.CATEGORIE, c, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"La configuration d'écriture de la base AUTRE est incomplète : la catégorie {missing[0]} n'est pas configurée.");
        var duplicate = configs.GroupBy(c => c.CATEGORIE, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"La configuration d'écriture contient plusieurs définitions pour la catégorie {duplicate.Key}.");
        if (configs.Count != Categories.Length)
            throw new InvalidOperationException("La configuration d'écriture contient des catégories inconnues.");
        var table = configs[0].TABLE_CIBLE;
        var matricule = configs[0].COL_MATRICULE;
        if (configs.Any(c => !string.Equals(c.TABLE_CIBLE, table, StringComparison.OrdinalIgnoreCase) || !string.Equals(c.COL_MATRICULE, matricule, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Toutes les catégories doivent utiliser la même table cible et la même colonne matricule.");
        ValidateIdentifier(table, "La table cible", allowQualifiedTable: true);
        ValidateIdentifier(matricule, "La colonne matricule");
        foreach (var config in configs)
        {
            NormalizeCategory(config.CATEGORIE);
            ValidateIdentifier(config.COL_VALEUR, $"La colonne de la catégorie {config.CATEGORIE}");
        }
    }

    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct) => uow.ExecuteSqlAsync("""
        IF OBJECT_ID(N'dbo.T_PAIE_ECRITURE', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.T_PAIE_ECRITURE (
                ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                BDD_SAGE nvarchar(128) NOT NULL,
                TABLE_CIBLE nvarchar(256) NOT NULL,
                COL_MATRICULE nvarchar(128) NOT NULL,
                CATEGORIE nvarchar(32) NOT NULL,
                COL_VALEUR nvarchar(128) NOT NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_ECRITURE_BDD_CATEGORIE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_ECRITURE'))
        BEGIN
            CREATE UNIQUE INDEX UX_T_PAIE_ECRITURE_BDD_CATEGORIE ON dbo.T_PAIE_ECRITURE (BDD_SAGE, CATEGORIE);
        END;
        """, ct);
}
