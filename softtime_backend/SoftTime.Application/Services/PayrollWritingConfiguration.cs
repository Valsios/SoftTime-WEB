using System.Text.RegularExpressions;

using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>Conventions partagées par la configuration et le writer de paie AUTRE.</summary>
public static class PayrollWritingConfiguration
{
    /// <summary>Les sept catégories d'heures supplémentaires (ordre historique), obligatoires par défaut.</summary>
    public static readonly string[] Categories =
        PayrollVariableCatalog.Toutes.Where(d => d.HeureSupplementaire).Select(d => d.Id).ToArray();

    /// <summary>Catégories qu'une ligne T_PAIE_ECRITURE peut porter : les variables envoyables du catalogue.</summary>
    public static IReadOnlyList<string> CategoriesConnues =>
        PayrollVariableCatalog.Toutes.Where(d => d.Envoyable).Select(d => d.Id).ToList();
    private static readonly Regex IdentifierPart = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Compteurs de lignes mises à jour par catégorie, pour le journal : toujours les sept catégories d'heures
    /// supplémentaires (synthèse inchangée par défaut), plus les autres variables envoyées, dans l'ordre du catalogue.
    /// </summary>
    public static Dictionary<string, int> CompteursParCategorie(PayrollVariableSelection selection) =>
        Categories.Concat(selection.Envoyees.Where(v => !Categories.Contains(v)))
            .ToDictionary(category => category, _ => 0, StringComparer.Ordinal);

    public static string NormalizeCategory(string? category) => CategoriesConnues.FirstOrDefault(c =>
        string.Equals(c, category?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"Catégorie de paie inconnue : '{category}'.");

    public static void ValidateIdentifier(string? value, string label, bool allowQualifiedTable = false)
    {
        var parts = (value ?? string.Empty).Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || (!allowQualifiedTable && parts.Length != 1) || (allowQualifiedTable && parts.Length > 2)
            || parts.Any(part => !IdentifierPart.IsMatch(part)))
            throw new DomainException($"{label} contient un identifiant SQL invalide.");
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

    public static void ValidateConfigurationRows(IReadOnlyList<PaieEcriture> configs)
        => ValidateConfigurationRows(configs, Categories);

    /// <summary>
    /// Contrôle de la configuration d'écriture AUTRE pour les catégories envoyées à la paie : seules les catégories
    /// actives doivent être configurées ; une catégorie inactive peut l'être ou non. Avec les sept catégories actives
    /// (valeur par défaut), contrôles et messages sont ceux d'origine.
    /// </summary>
    public static void ValidateConfigurationRows(IReadOnlyList<PaieEcriture> configs, IReadOnlyCollection<string> activeCategories)
    {
        var missing = CategoriesConnues.Where(activeCategories.Contains)
            .Where(c => !configs.Any(x => string.Equals(x.Categorie, c, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0)
            throw new DomainException($"La configuration d'écriture de la base AUTRE est incomplète : la catégorie {missing[0]} n'est pas configurée.");
        if (configs.Count == 0)
            throw new DomainException("La configuration d'écriture de la base AUTRE est vide.");
        var duplicate = configs.GroupBy(c => c.Categorie, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new DomainException($"La configuration d'écriture contient plusieurs définitions pour la catégorie {duplicate.Key}.");
        // Sans doublon, une ligne qui n'est pas une catégorie connue est inconnue (même règle que « 7 lignes exactement »).
        if (configs.Any(c => !CategoriesConnues.Any(k => string.Equals(k, c.Categorie, StringComparison.OrdinalIgnoreCase))))
            throw new DomainException("La configuration d'écriture contient des catégories inconnues.");
        var table = configs[0].TableCible;
        var matricule = configs[0].ColMatricule;
        if (configs.Any(c => !string.Equals(c.TableCible, table, StringComparison.OrdinalIgnoreCase) || !string.Equals(c.ColMatricule, matricule, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Toutes les catégories doivent utiliser la même table cible et la même colonne matricule.");
        ValidateIdentifier(table, "La table cible", allowQualifiedTable: true);
        ValidateIdentifier(matricule, "La colonne matricule");
        foreach (var config in configs)
        {
            NormalizeCategory(config.Categorie);
            ValidateIdentifier(config.ColValeur, $"La colonne de la catégorie {config.Categorie}");
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
