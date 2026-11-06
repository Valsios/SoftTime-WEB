namespace SoftTime.Application.DTOs;

public record DepartementServiceOptionsDto(IReadOnlyList<string> Departements, IReadOnlyList<string> Services);
