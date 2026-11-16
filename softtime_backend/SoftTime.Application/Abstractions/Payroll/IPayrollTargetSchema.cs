namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Colonne d'une table de la base de paie (INFORMATION_SCHEMA.COLUMNS).</summary>
public sealed record PayrollTargetColumn(string Nom, string Type, byte? Precision, int? Echelle);

/// <summary>
/// Schéma de la base de paie d'une base AUTRE (base active) : tables du schéma dbo et colonnes d'une table, avec leur type,
/// pour choisir les destinations des variables envoyées à la paie.
/// </summary>
public interface IPayrollTargetSchema
{
    Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken ct = default);

    /// <summary>Colonnes d'une table du schéma dbo ; liste vide si la table n'existe pas.</summary>
    Task<IReadOnlyList<PayrollTargetColumn>> ListColumnsAsync(string table, CancellationToken ct = default);
}
