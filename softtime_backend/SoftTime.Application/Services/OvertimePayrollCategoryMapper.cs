using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;

namespace SoftTime.Application.Services;

/// <summary>Valeur d'une catégorie d'écriture (variable de paie) dans une ligne EXO/IMPO, lue par le catalogue.</summary>
public static class OvertimePayrollCategoryMapper
{
    public static decimal ValueFor(string category, HsExoDto row)
        => PayrollVariableCatalog.Definition(PayrollWritingConfiguration.NormalizeCategory(category))
            .ValeurEnvoyee(PayrollRows.Valeurs(row));
}
