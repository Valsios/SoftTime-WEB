using SoftTime.Application.DTOs;

namespace SoftTime.Application.Services;

public static class OvertimePayrollCategoryMapper
{
    public static decimal ValueFor(string category, HsExoDto row) => PayrollWritingConfiguration.NormalizeCategory(category) switch
    {
        "Exo130" => row.Exo130 ?? 0,
        "Exo150" => row.Exo150 ?? 0,
        "I130" => row.I130 ?? 0,
        "I150" => row.I150 ?? 0,
        "Ferie" => row.Ferie ?? 0,
        "Dimanche" => row.Dim ?? 0,
        "Nuit" => row.Nuit ?? 0,
        _ => throw new InvalidOperationException("Catégorie inconnue.")
    };
}
