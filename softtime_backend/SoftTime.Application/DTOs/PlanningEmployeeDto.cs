namespace SoftTime.Application.DTOs;

public record PlanningEmployeeDto(int CardPaieId, string? Matricule, string? Nom, string? Prenom)
{
    public PlanningEmployeeDto() : this(default, default, default, default) { }
}
