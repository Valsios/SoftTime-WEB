namespace SoftTime.Application.DTOs;

/// <summary>Salarié + nombre de jours planifiés sur la période, pour la grille paginée du planning.</summary>
public record PlanningEmployeeStatDto(int CardPaieId, string? Matricule, string? Nom, string? Prenom, int PlannedDays)
{
    public PlanningEmployeeStatDto() : this(default, default, default, default, default) { }
}
