namespace SoftTime.Application.DTOs;

public record PayrollWriteResultDto(string Database, string TypeBase, int EmployeesUpdated, int CategoriesUpdated, string Message)
{
    /// <summary>STANDARD : mises à jour T_CUMSAL qui n'ont modifié aucune ligne (cumul absent dans SAGE).</summary>
    public int UpdatesWithoutRow { get; init; }

    /// <summary>Salariés du périmètre sans valeur dont les variables cochées ont été remises à 0 (inclus dans le total, pas dans <see cref="PayrollWriteResultDto.EmployeesUpdated"/>).</summary>
    public int EmployeesReset { get; init; }

    /// <summary>STANDARD : salariés sans valeur qui n'ont pas pu être remis à 0 (numéro de salarié introuvable dans SAGE) : leur ancien cumul reste dans SAGE.</summary>
    public int EmployeesNotReset { get; init; }
}
