using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>
/// Montants d'un salarié à reporter en paie : les valeurs de sa ligne de résultat, lues variable par variable par
/// <see cref="PayrollVariableCatalog"/> (une valeur absente vaut zéro).
/// </summary>
public sealed record OvertimeAmounts(PayrollRowValues Valeurs)
{
    /// <summary>Montants d'une ligne EXO/IMPO.</summary>
    public static OvertimeAmounts From(HsExoDto hs) => new(PayrollRows.Valeurs(hs));

    /// <summary>Valeur envoyée d'une variable (zéro si absente).</summary>
    public decimal ValeurDe(string variable) => PayrollVariableCatalog.Definition(variable).ValeurEnvoyee(Valeurs);
}

/// <summary>Correspondance entre une ligne EXO/IMPO et les valeurs des variables de paie.</summary>
public static class PayrollRows
{
    public static PayrollRowValues Valeurs(HsExoDto hs) => new(
        hs.Exo130, hs.Exo150, hs.I130, hs.I150, hs.Ferie, hs.Nuit, hs.Dim, hs.Retard, hs.Absence);
}
