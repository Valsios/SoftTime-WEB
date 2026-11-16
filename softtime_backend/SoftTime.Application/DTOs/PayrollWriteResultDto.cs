namespace SoftTime.Application.DTOs;

public record PayrollWriteResultDto(string Database, string TypeBase, int EmployeesUpdated, int CategoriesUpdated, string Message)
{
    /// <summary>STANDARD : mises à jour T_CUMSAL qui n'ont modifié aucune ligne (cumul absent dans SAGE).</summary>
    public int UpdatesWithoutRow { get; init; }
}
