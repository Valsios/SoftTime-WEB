namespace SoftTime.Application.DTOs;

public record AffectationOptionsDto(
    IReadOnlyList<string> Departements,
    IReadOnlyList<string> Services);
