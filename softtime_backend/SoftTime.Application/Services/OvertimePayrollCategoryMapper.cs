using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;

namespace SoftTime.Application.Services;

/// <summary>Valeur d'une catégorie d'écriture (variable de paie) dans une ligne EXO/IMPO, lue par le catalogue.</summary>
public static class OvertimePayrollCategoryMapper
{
    public static decimal ValueFor(string category, HsExoDto row)
        => PayrollVariableCatalog.Definition(PayrollWritingConfiguration.NormalizeCategory(category))
            .ValeurEnvoyee(PayrollRows.Valeurs(row));

    /// <summary>Valeur écrite selon la configuration de la base : en jours pour une variable envoyée en jours (Absence).</summary>
    public static decimal ValueFor(string category, HsExoDto row, PayrollVariableSelection selection)
        => selection.ValeurEnvoyee(PayrollWritingConfiguration.NormalizeCategory(category), PayrollRows.Valeurs(row));
}
