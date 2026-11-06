namespace SoftTime.Application.DTOs;

/// <summary>Score et répartition d'un département (<c>null</c> : salariés sans département).</summary>
public sealed record ClosingDepartmentDto(
    string? Departement,
    int? Score,
    int JoursEmployeControles,
    int NombreAnomalies,
    IReadOnlyList<ClosingCountDto> ParGravite,
    IReadOnlyList<ClosingCountDto> ParType);
